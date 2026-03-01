using System.Text.Json;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Settings;
using NLog;
using StackExchange.Redis;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient.Models.Events;
using Microsoft.EntityFrameworkCore;
using RandN;
using RandN.Compat;

namespace KfChatDotNetBot.Services;

public class KasinoShop
{
    private static RandomShim<StandardRng> _rand = RandomShim.Create(StandardRng.Create());
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private IDatabase? _redisDb;
    public static ChatBot BotInstance = null!;
    public int[]? activeLoanIds = null;
    public Dictionary<int, KasinoShopProfile> Gambler_Profiles = new(); //list of all profiles, accesesd via kf user id
    public decimal DefaultHouseEdgeModifier = 0;
    
    public KasinoShop(ChatBot kfChatBot)
    {
        BotInstance = kfChatBot;
        
        var connectionString = SettingsProvider.GetValueAsync(BuiltIn.Keys.BotRedisConnectionString).Result;
        if (string.IsNullOrEmpty(connectionString.Value))
        {
            _logger.Error($"Can't initialize the Kasino Mines service as Redis isn't configured in {BuiltIn.Keys.BotRedisConnectionString}");
            return;
        }

        var redis = ConnectionMultiplexer.Connect(connectionString.Value);
        _redisDb = redis.GetDatabase();
        
        LoadProfiles();
        
    }

    public async void LoadProfiles()
    {
        if (_redisDb == null) throw new InvalidOperationException("Kasino shop service isn't initialized");
        var json = await _redisDb.StringGetAsync($"Shop.Profiles.State");
        var json2 = await _redisDb.StringGetAsync($"Shop.LoanIds.State");
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            var options = new JsonSerializerOptions{IncludeFields = true};
            Gambler_Profiles = JsonSerializer.Deserialize<Dictionary<int, KasinoShopProfile>>(json.ToString(), options) ??
                          throw new InvalidOperationException();
            activeLoanIds = JsonSerializer.Deserialize<int[]>(json2.ToString(), options) ?? throw new InvalidOperationException();
        }
        catch (Exception e)
        {
            _logger.Error(e);
            _logger.Error("Potentially failed to deserialize kasinoshop details");
            Gambler_Profiles = new Dictionary<int, KasinoShopProfile>();
            activeLoanIds = new int[0];
        }
    }

    public async Task SaveProfiles()
    {
        if (_redisDb == null) throw new InvalidOperationException("Kasino mines service isn't initialized");
        var options = new JsonSerializerOptions
        {
            IncludeFields = true,
            WriteIndented = false
        };
        var json = JsonSerializer.Serialize(Gambler_Profiles, options);
        var json2 = JsonSerializer.Serialize(activeLoanIds, options);
        await _redisDb.StringSetAsync($"Shop.Profiles.State", json, null, When.Always);
        await _redisDb.StringSetAsync($"Shop.LoanIds.State", json2, null, When.Always);
    }

    public async Task ResetProfiles()
    {
        Gambler_Profiles = new Dictionary<int, KasinoShopProfile>();
        activeLoanIds = new int[0];
        await SaveProfiles();
    }


    public async Task PrintBalance(GamblerDbModel gambler)
    {
        await BotInstance.SendChatMessageAsync($"{Gambler_Profiles[gambler.User.KfId].FormatBalanceAsync()}", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
    }
    
    public async Task ResetAllLoans()
    {
        foreach (var key in Gambler_Profiles.Keys)
        {
            Gambler_Profiles[key].Loans.Clear();
        }
        await SaveProfiles();
    }

    public async Task GetCurrentRiggingState()
    {
        string str = "";
        List<decimal> values = new() { 1.02m, -0.9m, 0 };
        List<decimal> differences = new() {values[0] - DefaultHouseEdgeModifier, values[1] - DefaultHouseEdgeModifier, values[2] - DefaultHouseEdgeModifier };
        if (DefaultHouseEdgeModifier == 1.02m)
        {
            await BotInstance.SendChatMessageAsync("The switch was flipped twice.", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
            return;
        }
        else if (DefaultHouseEdgeModifier == -0.9m)
        {
            await BotInstance.SendChatMessageAsync("The switch was flipped once.", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
            return;
        }
        else if (DefaultHouseEdgeModifier == 0)
        {
            await BotInstance.SendChatMessageAsync("The button is pressed.", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
            return;
        }
        
        var currentDHEM = DefaultHouseEdgeModifier;
        
        
        while (currentDHEM != 0)
        {
            var ld = LowestDifferenceIndex(differences);
            if (ld.i == 0)
            {
                str += "A switch was flipped twice.[br]";
                currentDHEM -= values[ld.i];
            }
            else if (ld.i == 1)
            {
                str += "A switch was flipped once.[br]";
                currentDHEM -= values[ld.i];
            }
            else if (ld.i == 2)
            {
                if (ld.d < 0)
                {
                    currentDHEM += 0.01m;
                    str += "A dial was moved down.[br]";
                }
                else
                {
                    currentDHEM -= 0.01m;
                    str += "A dial was moved up.[br]";
                }
            }
            differences = new() {values[0] - DefaultHouseEdgeModifier, values[1] - DefaultHouseEdgeModifier, values[2] - DefaultHouseEdgeModifier };
        }
        await BotInstance.SendChatMessageAsync(str, true, autoDeleteAfter: TimeSpan.FromSeconds(10));

        (int i, decimal d) LowestDifferenceIndex(List<decimal> diffs)
        {
            var lowestDifference = Math.Abs(differences[0]);
            var lowestDifferenceIndex = 0;
            if (Math.Abs(diffs[1]) < lowestDifference)
            {
                lowestDifference = Math.Abs(differences[1]);
                lowestDifferenceIndex = 1;
            }

            if (Math.Abs(diffs[2]) < lowestDifference)
            {
                lowestDifference = Math.Abs(differences[2]);
                lowestDifferenceIndex = 2;
            }
            
            return (lowestDifferenceIndex, lowestDifference);
        }
    }
    public async Task ProcessRigging(Rigging type, decimal num = -1, bool? dial = null)
    {
        /*
         * FLIP SWITCH - variate default house edge modifier to -0.9, then to 1.02 if it's currently -0.9
         *
         * PUSH BUTTON - reset house edge modifier to 0
         *
         * DIAL UP/DOWN - decrease or increase default house edge modifier by 0.01
         *
         * PULL LEVER - does nothing
         *
         * KEYPAD <num> - sets the house edge modifier to num
         */
        var oldDefault = DefaultHouseEdgeModifier;
        decimal difference = 0;
        switch (type)
        {
            case Rigging.Lever:
                await BotInstance.SendChatMessageAsync("A lever was pulled.", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
                await Task.Delay(TimeSpan.FromMinutes(5));
                await BotInstance.SendChatMessageAsync("A lever returned to its original position.", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
                break;
            case Rigging.Keypad:
                if (num == -1) throw new Exception("Invalid number passed into keypad");
                DefaultHouseEdgeModifier = num;
                difference = DefaultHouseEdgeModifier - oldDefault;
                await BotInstance.SendChatMessageAsync("Keypad value accepted.", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
                break;
            case Rigging.Switch:
                if (DefaultHouseEdgeModifier != -0.9m)
                {
                    DefaultHouseEdgeModifier = -0.9m;
                }
                else DefaultHouseEdgeModifier = 1.02m;
                difference = DefaultHouseEdgeModifier - oldDefault;
                await BotInstance.SendChatMessageAsync("A switch was flipped.", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
                break;
            case Rigging.Button:
                DefaultHouseEdgeModifier = 0;
                difference = DefaultHouseEdgeModifier - oldDefault;
                break;
            case Rigging.Dial:
                if (dial == null) throw new Exception("Invalid dial value passed into dial");
                if (dial.Value)
                {
                    DefaultHouseEdgeModifier += 0.01m;
                }
                else DefaultHouseEdgeModifier -= 0.01m;
                difference = DefaultHouseEdgeModifier - oldDefault;
                break;
        }
        foreach (var key in Gambler_Profiles.Keys)
        {
            Gambler_Profiles[key].HouseEdgeModifier += difference;
        }
        await SaveProfiles();
    }

    public decimal GetCurrentCrackPrice(GamblerDbModel gambler)
    {
        return CrackPrice * Gambler_Profiles[gambler.User.KfId].CrackCounter;
    }
    
    public async Task PrintDrugMarket(GamblerDbModel gambler)
    {
        int cc = Gambler_Profiles[gambler.User.KfId].CrackCounter;
        List<string> drugs = new();
        drugs.Add($"1. Crack: {await (CrackPrice * cc).FormatKasinoCurrencyAsync()} per dose");
        drugs.Add($"2. Weed: {await WeedPricePerHour.FormatKasinoCurrencyAsync()} per hour");
        if (Gambler_Profiles[gambler.User.KfId].FloorNugs > 0)
        {
            drugs.Add($"3. Floor Nugs: {Gambler_Profiles[gambler.User.KfId].FloorNugs}");
        }
    }

    public bool CheckWagerReq(GamblerDbModel gambler)
    {
        if (Gambler_Profiles[gambler.User.KfId].SponsorWagerLock[0] >
            Gambler_Profiles[gambler.User.KfId].SponsorWagerLock[1]) return false;
        return true;
    }

    public decimal RemainingWagerReq(GamblerDbModel gambler)
    {
        return Gambler_Profiles[gambler.User.KfId].SponsorWagerLock[0] - Gambler_Profiles[gambler.User.KfId].SponsorWagerLock[1];
    }
    
    public async Task ProcessDrugUse(GamblerDbModel gambler, decimal amount, int drug)
    {
        Dictionary<int, string> drugs = new()
        {
            {1, "Crack"},
            {2, "Weed"},
            {3, "Floor Nugs"}
        };
        if (drug != 3)
        {
            if (Gambler_Profiles[gambler.User.KfId].Balance()[0] < amount)
            {
                await BotInstance.SendChatMessageAsync(
                    $"{gambler.User.FormatUsername()}, you can't afford to buy {await amount.FormatKasinoCurrencyAsync()} worth of {drugs[drug]}. {await Gambler_Profiles[gambler.User.KfId].FormatBalanceAsync()}", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
                return;
            }
        }
        else
        {
            if (Gambler_Profiles[gambler.User.KfId].FloorNugs < amount)
            {
                await BotInstance.SendChatMessageAsync($"{gambler.User.FormatUsername()}, you only have {Gambler_Profiles[gambler.User.KfId].FloorNugs} floor nugs, so that's all you could smoke right now.", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
                amount = Gambler_Profiles[gambler.User.KfId].FloorNugs;
            }
        }

        if (drug == 1)
        {
            _ = Gambler_Profiles[gambler.User.KfId].SmokeCrack();
        }
        else if (drug == 2)
        {
            TimeSpan weedDuration = TimeSpan.FromHours((double)(amount / WeedPricePerHour));
            _ = Gambler_Profiles[gambler.User.KfId].SmokeWeed(weedDuration);
        }
        else
        {
            TimeSpan dur = TimeSpan.FromMinutes(6 * (int)amount);
            _ = Gambler_Profiles[gambler.User.KfId].SmokeWeed(dur);
        }
    }

    public async Task<bool> ProcessLoan(int receiverKfId, decimal amount, GamblerDbModel gUser, int senderGamblerId)
    {
        var sender = gUser.User;
        //check if the person they tried to loan to is able to get a loan
        await using var db = new ApplicationDbContext();
        var targetUser = await db.Users.FirstOrDefaultAsync(u => u.KfId == receiverKfId);
        if (targetUser == null)
        {
            await BotInstance.SendChatMessageAsync($"{sender.FormatUsername()}, user with KF ID {receiverKfId} not found.", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
            return false;
        }
        if (!Gambler_Profiles[receiverKfId].IsLoanable)
        {
            await BotInstance.SendChatMessageAsync(
                $"{sender.FormatUsername()}, {targetUser.FormatUsername()} is not loanable, they need to beg for a loan first.", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
            return false;
        }
        //check if the loaner has enough crypto to send loan
        if (Gambler_Profiles[sender.KfId].Balance()[1] < amount)
        {
            await BotInstance.SendChatMessageAsync($"{sender.FormatUsername()}, you don't have enough krypto to loan {targetUser.FormatUsername()}. {amount}. {await Gambler_Profiles[sender.KfId].FormatBalanceAsync()}", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
            return false;
        }
        
        Random rand = new Random();
        int loanId = (int)(1000000000 * rand.NextDouble());
        if (activeLoanIds == null) activeLoanIds = new int[1];
        else
        {
            int[] newLoans = new int[activeLoanIds.Length + 1];
            activeLoanIds.CopyTo(newLoans, 1);
            newLoans[0] = loanId;
            activeLoanIds = newLoans;
        }
        await using var gamblerdb = new ApplicationDbContext();
        var targetgambler = await gamblerdb.Gamblers.FirstOrDefaultAsync(g => g.User.KfId == Gambler_Profiles[receiverKfId].GamblerId);
        if (targetgambler == null)
        {
            await BotInstance.SendChatMessageAsync($"{sender.FormatUsername()}, gambler profile for user with KF ID {receiverKfId} not found.", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
            return false;
        }
        //public Loan(decimal amount, int payableToGambler, int payableToKf, int recieverGambler, int recieverKf, int Id)
        Loan loan = new Loan(amount, senderGamblerId, sender.Id, targetgambler.Id, receiverKfId, loanId, sender);
        //take from senders crypto balance and deposit into receivers kasino balance
        Gambler_Profiles[sender.KfId].ModifyBalance(-amount);
        await Money.ModifyBalanceAsync(targetgambler.Id, amount, TransactionSourceEventType.Loan);
        Gambler_Profiles[sender.KfId].Loans.Add(loanId, loan);
        Gambler_Profiles[receiverKfId].Loans.Add(loanId, loan);
        Gambler_Profiles[receiverKfId].OutstandingLoanBalance += amount;
        Gambler_Profiles[receiverKfId].IsLoanable = false;
        Gambler_Profiles[receiverKfId].KreditScore -= Convert.ToInt32(amount / 2);
        await SaveProfiles();
        return true;
    }

    public async Task PrintSmashableShop(GamblerDbModel gambler)
    {
        string str = "";
        int counter = 1;
        foreach (var type in Enum.GetValues<SmashableType>())
        {
            str += $"{counter}: {type} - $10000 KKK[br]";
        }
    }

    public async Task ProcessSmashablePurchase(GamblerDbModel gambler, int type)
    {
        var smashTypes = Enum.GetValues<SmashableType>();
        type--;
        if (type < 0 || type > smashTypes.Length - 1)
        {
            await BotInstance.SendChatMessageAsync(
                $"{gambler.User.FormatUsername()}, invalid smashable type. 1 - {smashTypes.Length}", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
            return;
        }
        if (Gambler_Profiles[gambler.User.KfId].Balance()[1] < 10000)
        {
            await BotInstance.SendChatMessageAsync(
                $"{gambler.User.FormatUsername()}, you don't have enough krypto to buy smashables. {await Gambler_Profiles[gambler.User.KfId].FormatBalanceAsync()}");
            return;
        }
        var id = GenerateRandomId(gambler);
        Gambler_Profiles[gambler.User.KfId].Assets.Add(id, new Smashable(id, 10000, smashTypes[type]));
        Gambler_Profiles[gambler.User.KfId].ModifyBalance(-10000);
    }
    public async Task ProcessRepayment(GamblerDbModel payerGambler, UserDbModel payer, int payeeKfId, decimal amount) //loans can be repaid from crypto balance and kasino balance. it prefers to take from your crypto balance first if you have any
    {
        decimal payerTotalBalance = payerGambler.Balance + Gambler_Profiles[payer.KfId].Balance()[0];
        if (payerTotalBalance < amount)
        {
            await BotInstance.SendChatMessageAsync($"{payer.FormatUsername()}, you don't have enough to repay {await amount.FormatKasinoCurrencyAsync()}. Your total balance: {await payerTotalBalance.FormatKasinoCurrencyAsync()}");
            return;
        }

        
        int loanId = -1;
        //find the loan
        foreach (var loan in Gambler_Profiles[payeeKfId].Loans.Values)
        {
            if (loan.payableToKf == payer.KfId)
            {
                loanId = loan.Id;
                break;
            }
        }

        if (loanId == -1)
        {
            await BotInstance.SendChatMessageAsync($"{payer.FormatUsername()}, you don't have a loan with {payeeKfId}.");
            return;
        }
        var theloan = Gambler_Profiles[payeeKfId].Loans[loanId];
        //compare the amount paid to the amount of the loan
        if (amount >= theloan.payoutAmount)
        {
            //if the amount is more or equal, clear the loan when you pay, and give the payer credit score bonus for repaying the loan
            amount = theloan.payoutAmount;
            Gambler_Profiles[payer.KfId].KreditScore += Convert.ToInt32(Gambler_Profiles[payer.KfId].Loans[loanId].amount * 3 / 4);
            Gambler_Profiles[payer.KfId].Loans.Remove(loanId);
            Gambler_Profiles[payeeKfId].Loans.Remove(loanId);
            var newLoans = new int[activeLoanIds!.Length - 1];
            if (activeLoanIds.Length > 1)
            {
                for (int i = 0; i < activeLoanIds.Length - 1; i++)
                {
                    if (activeLoanIds[i] != loanId) newLoans[i] = activeLoanIds[i];
                }
            }
            activeLoanIds = newLoans;
        }
        else if (amount < Gambler_Profiles[payeeKfId].Loans[loanId].payoutAmount)
        {
            Gambler_Profiles[payeeKfId].Loans[loanId].payoutAmount -= amount;
        }
        //split the amount from crypto and kasino balance as available
        decimal takeFromCrypto = 0;
        decimal takeFromKasino = 0;

        if (amount > Gambler_Profiles[payeeKfId].Balance()[0])
        {
            //if the amount is more than the amount of crypto you have, take from kasino balance as well
            takeFromCrypto = Gambler_Profiles[payeeKfId].Balance()[0];
            takeFromKasino = amount - takeFromCrypto;
        }
        else takeFromCrypto = amount;
        //process the payments
        Gambler_Profiles[payer.KfId].ModifyBalance(-takeFromCrypto);
        await Money.ModifyBalanceAsync(payerGambler.Id, -takeFromKasino, TransactionSourceEventType.Loan);
        Gambler_Profiles[payeeKfId].ModifyBalance(amount);
        await SaveProfiles();
    }

    public async Task PrintLoansList(GamblerDbModel gambler)
    {
        var user = gambler.User;
        string message = $"{user.FormatUsername()}";
        var msg = await BotInstance.SendChatMessageAsync($"{message}", true);
        await BotInstance.WaitForChatMessageAsync(msg);
        if (Gambler_Profiles[user.KfId].Loans.Count == 0)
        {
            message += " is debt free!";
            await BotInstance.KfClient.EditMessageAsync(msg.ChatMessageUuid, message);
            await Task.Delay(TimeSpan.FromSeconds(10));
            await BotInstance.KfClient.DeleteMessageAsync(msg.ChatMessageUuid);
            return;
        }

        foreach (var loan in Gambler_Profiles[user.KfId].Loans.Values)
        {
            message += $"[br]{await loan.ToStringAsync(gambler.User.KfId)}";
            await BotInstance.KfClient.EditMessageAsync(msg.ChatMessageUuid, message);
            await Task.Delay(10);
        }
        await Task.Delay(TimeSpan.FromSeconds(10));
        await BotInstance.KfClient.DeleteMessageAsync(msg.ChatMessageUuid);
    }

    
    public async Task ProcessBeg(GamblerDbModel gambler)
    {
        var user = gambler.User;
        _ = Gambler_Profiles[user.KfId].Beg(user);
    }

    public async Task ProcessWithdraw(GamblerDbModel gambler, decimal amount)
    {
        int kfId = gambler.User.KfId;
        if (amount > gambler.Balance)
        {
            await BotInstance.SendChatMessageAsync($"{gambler.User.FormatUsername()}, you don't have enough to withdraw {await amount.FormatKasinoCurrencyAsync()}. Balance: {await gambler.Balance.FormatKasinoCurrencyAsync()}", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
            return;
        }

        Gambler_Profiles[gambler.User.KfId].Withdraw(amount);
        var newBalance = await Money.ModifyBalanceAsync(gambler.Id, amount, TransactionSourceEventType.Withdraw);
        await SaveProfiles();
        await BotInstance.SendChatMessageAsync(
            $"{gambler.User.FormatUsername()}, you withdrew {await amount.FormatKasinoCurrencyAsync()} to your krypto balance. Kasino Balance: {await newBalance.FormatKasinoCurrencyAsync()} | {await Gambler_Profiles[kfId].FormatBalanceAsync()}",
            true, autoDeleteAfter: TimeSpan.FromSeconds(10));
        
    }

    public async Task ProcessDeposit(GamblerDbModel gambler, decimal amount)
    {
        int kfId = gambler.User.KfId;
        if (amount > Gambler_Profiles[kfId].Balance()[0])
        {
            await BotInstance.SendChatMessageAsync(
                $"{gambler.User.FormatUsername()}, you don't have enough krypto to deposit {await amount.FormatKasinoCurrencyAsync()}. {await Gambler_Profiles[kfId].FormatBalanceAsync()}");
            return;
        }
        var newBalance = await Money.ModifyBalanceAsync(gambler.Id, amount, TransactionSourceEventType.Deposit);
        Gambler_Profiles[kfId].Deposit(amount);
        await SaveProfiles();
        await BotInstance.SendChatMessageAsync(
            $"{gambler.User.FormatUsername()}, you deposited {await amount.FormatKasinoCurrencyAsync()} to your kasino balance. Kasino Balance: {await newBalance.FormatKasinoCurrencyAsync()} | {await Gambler_Profiles[kfId].FormatBalanceAsync()}",
            true, autoDeleteAfter: TimeSpan.FromSeconds(10));

    }

    public async Task ProcessInvestment(GamblerDbModel gambler, int item, decimal amount)
    {
        if (amount > Gambler_Profiles[gambler.User.KfId].Balance()[1])
        {
            await BotInstance.SendChatMessageAsync(
                $"{gambler.User.FormatUsername()}, you can't afford this investment. {await Gambler_Profiles[gambler.User.KfId].FormatBalanceAsync()}", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
            return;
        }

        if (item < 1 || item > 3)
        {
            await BotInstance.SendChatMessageAsync($"{gambler.User.FormatUsername()}, invalid investment choice. 1 - gold, 2 - silver, 3 - house.",true, autoDeleteAfter: TimeSpan.FromSeconds(10));
            return;
        }

        int id = GenerateRandomId(gambler);

        string str;
        switch (item)
        {
            case 1:
                str = Money.GetRandomDouble(gambler) < 0.5 ? "chain" : "coin";
                Investment newGold = new Investment(id, amount, GoldInterestRange, InvestmentType.Gold, $"Gold {str}");
                Gambler_Profiles[gambler.User.KfId].Assets.Add(id, newGold);
                break;
            case 2:
                str = Money.GetRandomDouble(gambler) < 0.5 ? "chain" : "coin";
                Investment newSilver = new Investment(id, amount, SilverInterestRange, InvestmentType.Silver, $"Silver {str}");
                Gambler_Profiles[gambler.User.KfId].Assets.Add(id, newSilver);
                break;
            case 3:
                Investment newHouse = new Investment(id, amount, HouseInterestRange, InvestmentType.House, "House");
                Gambler_Profiles[gambler.User.KfId].Assets.Add(id, newHouse);
                break;
        }

        await SaveProfiles();
    }

    

    public async Task ProcessCarPurchase(GamblerDbModel gambler, int carId)
    {
        //civic audi bentley bmw
        foreach (var asset in Gambler_Profiles[gambler.User.KfId].Assets.Values)
        {
            if (asset is Car c)
            {
                await BotInstance.SendChatMessageAsync(
                    $"{gambler.User.FormatUsername()}, you can't buy a car you already have one: {c}.", true,
                    autoDeleteAfter: TimeSpan.FromSeconds(10));
                return;
            }
        }
        var car = DefaultCars.ElementAt(carId).Value;
        car.SetId(gambler);
        Gambler_Profiles[gambler.User.KfId].Assets.Add(car.Id, car);
        await BotInstance.SendChatMessageAsync($"{gambler.User.FormatUsername()}, you bought {car}", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
        await SaveProfiles();
    }

    public void ProcessLossBackTracking(GamblerDbModel gambler, decimal amount)
    {
        Gambler_Profiles[gambler.User.KfId].Tracker.AddLossback(amount);
    }
    
    public async Task ProcessWorkJob(GamblerDbModel gambler)
    {
        bool hasCar = false;
        int carId = -1;
        foreach (var assetKey in Gambler_Profiles[gambler.User.KfId].Assets.Keys)
        {
            if (Gambler_Profiles[gambler.User.KfId].Assets[assetKey] is Car car)
            {
                carId = assetKey;
                hasCar = true;
                break;
            }
        }

        if (!hasCar)
        {
            await BotInstance.SendChatMessageAsync($"{gambler.User.FormatUsername()}, you don't have a car to get a job with.", true, autoDeleteAfter:TimeSpan.FromSeconds(10));
            return;
        }

        await ((Car)(Gambler_Profiles[gambler.User.KfId].Assets[carId])).ProcessWorkJob(gambler);
        await SaveProfiles();
    }
    public async Task ProcessWagerTracking(GamblerDbModel gambler, WagerGame game, decimal amount, decimal net, decimal newBalance)
    {
        Gambler_Profiles[gambler.User.KfId].Tracker.AddWager(game, amount, net);
        if (newBalance < 1 && Gambler_Profiles[gambler.User.KfId].SponsorWagerLock[0] > 0) //if you ran out of money after that gamble reset your wager lock
        {
            Gambler_Profiles[gambler.User.KfId].SponsorWagerLock = new decimal[] { 0, 0 };
        }

        if (Gambler_Profiles[gambler.User.KfId].SponsorWagerLock[0] >
            Gambler_Profiles[gambler.User.KfId].SponsorWagerLock[1])
        {
            Gambler_Profiles[gambler.User.KfId].SponsorWagerLock = new decimal[] { 0, 0 };
            await BotInstance.SendChatMessageAsync(
                $"{gambler.User.FormatUsername()}, you have reached the wager requirement for your sponsorship!", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
        }
        await SaveProfiles();
    }
    
    public async Task ProcessJuicerOrRainTracking(GamblerDbModel sender, GamblerDbModel reciever, decimal amountPerReciever)
    {
        Gambler_Profiles[sender.User.KfId].Tracker.AddWithdrawal(amountPerReciever);
        if (Gambler_Profiles.ContainsKey(reciever.User.KfId)) Gambler_Profiles[reciever.User.KfId].Tracker.AddDeposit(amountPerReciever);
        if (Gambler_Profiles.ContainsKey(sender.User.KfId)) Gambler_Profiles[sender.User.KfId].Tracker.AddWithdrawal(amountPerReciever);
        await SaveProfiles();
    }
    
    public async Task ProcessStake(GamblerDbModel gambler, decimal amount)
    {
        //check if they have enough crypto for the stake
        if (Gambler_Profiles[gambler.User.KfId].Balance()[1] < amount)
        {
            await BotInstance.SendChatMessageAsync(
                $"{gambler.User.FormatUsername()}, you don't have enough krypto to stake {await amount.FormatKasinoCurrencyAsync()}. {await Gambler_Profiles[gambler.User.KfId].FormatBalanceAsync()}",true, autoDeleteAfter: TimeSpan.FromSeconds(10));
            return;
        }

        int id = GenerateRandomId(gambler);

        var stake = new Investment(id, amount, CryptoStakeInterestRange, InvestmentType.Stake, "Stake");
        Gambler_Profiles[gambler.User.KfId].Assets.Add(id, stake);
        Gambler_Profiles[gambler.User.KfId].ModifyBalance(-amount);
        await BotInstance.SendChatMessageAsync(
            $"{gambler.User.FormatUsername()}, you staked {await amount.FormatKasinoCurrencyAsync()} krypto.[br] {Gambler_Profiles[gambler.User.KfId].Assets[id]} {await Gambler_Profiles[gambler.User.KfId].FormatBalanceAsync()}");
        await SaveProfiles();
    }

    public async Task ProcessSponsorship(GamblerDbModel gambler)
    {
        if (Gambler_Profiles[gambler.User.KfId].IsSponsored)
        {
            await BotInstance.SendChatMessageAsync($"{gambler.User.FormatUsername()}, you are already sponsored.", true,
                autoDeleteAfter: TimeSpan.FromSeconds(10));
            return;
        }
        Gambler_Profiles[gambler.User.KfId].IsSponsored = true;
        await BotInstance.SendChatMessageAsync(
            $"{gambler.User.FormatUsername()}, you are now sponsored! You can claim your bonus every day with !sponsor bonus", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
        await SaveProfiles();
    }

    public async Task ProcessSponsorshipEnd(GamblerDbModel gambler)
    {
        Gambler_Profiles[gambler.User.KfId].IsSponsored = false;
        await BotInstance.SendChatMessageAsync(
            $"{gambler.User.FormatUsername()}, you are no longer sponsored!", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
        await SaveProfiles();
    }

    public async Task ProcessSponsorBonus(GamblerDbModel gambler)
    {
        Gambler_Profiles[gambler.User.KfId].ProcessSponsorBonus(KASINO_SPONSOR_BONUS);
        Gambler_Profiles[gambler.User.KfId].SponsorWagerLock[1] += KASINO_SPONSOR_BONUS;
        Gambler_Profiles[gambler.User.KfId].lastSponsorBonus = DateTime.UtcNow;
        var newBalance = await Money.ModifyBalanceAsync(gambler.Id, KASINO_SPONSOR_BONUS, TransactionSourceEventType.Sponsorship);
        await BotInstance.SendChatMessageAsync(
            $"{gambler.User.FormatUsername()}, you claimed your sponsor bonus of {await KASINO_SPONSOR_BONUS.FormatKasinoCurrencyAsync()}. Balance: {await newBalance.FormatKasinoCurrencyAsync()}");
        await SaveProfiles();
    }
    public async Task UnStake(GamblerDbModel gambler, decimal amount = -1, int assetId = -1, bool all = false)
    {
        bool noId = assetId == -1;
        //check if they have a stake
        int stakeCounter = 0;
        bool validId = false;
        foreach (var asset in Gambler_Profiles[gambler.User.KfId].Assets.Values)
        {
            if (asset is Investment inv && inv.investment_type == InvestmentType.Stake)
            {
                stakeCounter++;
                if (assetId == -1) assetId = inv.Id;
                if (assetId == inv.Id) validId = true;
            }
        }
        if (stakeCounter == 0)
        {
            await BotInstance.SendChatMessageAsync($"{gambler.User.FormatUsername()}, you don't have a stake to unstake.", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
            return;
        }

        if (!validId)
        {
            await BotInstance.SendChatMessageAsync($"{gambler.User.FormatUsername()}, you don't have a stake with that ID.", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
            return;
        }
        
        Investment stake;
        int cooldown;
        decimal value;
        decimal totalStakedValue = 0;
        if (all || amount >= totalStakedValue)
        {
            bool success = false;
            foreach (var asset in Gambler_Profiles[gambler.User.KfId].Assets.Values)
            {
                if (asset is Investment inv && inv.investment_type == InvestmentType.Stake)
                {
                    stake = inv;
                    value = stake.GetCurrentValue();
                    totalStakedValue += value;
                    cooldown = (DateTime.UtcNow - inv.acquired).Days;
                    if (cooldown > 7)
                    {
                        success = true;
                        Gambler_Profiles[gambler.User.KfId].Assets.Remove(asset.Id);
                        Gambler_Profiles[gambler.User.KfId].ModifyBalance(value);
                        await BotInstance.SendChatMessageAsync(
                            $"{gambler.User.FormatUsername()} unstaked {stake}. {await Gambler_Profiles[gambler.User.KfId].FormatBalanceAsync()}");
                        
                    }
                }
            }

            if (!success)
            {
                await BotInstance.SendChatMessageAsync($"{gambler.User.FormatUsername()}, you don't have any stakes that are ready to be unstaked.", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
                return;
            }
        }
        else if (stakeCounter == 1)
        {
            stake = (Investment)Gambler_Profiles[gambler.User.KfId].Assets[assetId];
            value = stake.GetCurrentValue();
            cooldown = (DateTime.UtcNow - stake.acquired).Days;
            if (cooldown < 7)
            {
                await BotInstance.SendChatMessageAsync(
                    $"{gambler.User.FormatUsername()}, you can't unstake your stake yet, {7-cooldown} days until it unlocks.");
            }

            if (amount == -1 || amount >= value)
            {
                //unstake the whole thing if no amount or if amount is greater than its value
                Gambler_Profiles[gambler.User.KfId].Assets.Remove(assetId);
                Gambler_Profiles[gambler.User.KfId].ModifyBalance(value);
                await BotInstance.SendChatMessageAsync(
                    $"{gambler.User.FormatUsername()} unstaked {stake}. {await Gambler_Profiles[gambler.User.KfId].FormatBalanceAsync()}");
            }
            else
            {
                ((Investment)Gambler_Profiles[gambler.User.KfId].Assets[assetId]).StakePartialSale(amount);
                stake = ((Investment)Gambler_Profiles[gambler.User.KfId].Assets[assetId]);
                Gambler_Profiles[gambler.User.KfId].ModifyBalance(amount);
                await BotInstance.SendChatMessageAsync(
                    $"{gambler.User.FormatUsername()} partially unstaked {stake}. {await Gambler_Profiles[gambler.User.KfId].FormatBalanceAsync()}");
            }
            
        }
        else //if you have multiple stakes
        {
            stake = (Investment)Gambler_Profiles[gambler.User.KfId].Assets[assetId];
            value = stake.GetCurrentValue();
            if (amount == -1)
            {
                //unstake whole stake based on the id
                Gambler_Profiles[gambler.User.KfId].Assets.Remove(assetId);
                Gambler_Profiles[gambler.User.KfId].ModifyBalance(value);
                await BotInstance.SendChatMessageAsync(
                    $"{gambler.User.FormatUsername()} unstaked {stake}. {await Gambler_Profiles[gambler.User.KfId].FormatBalanceAsync()}");
            }
            else
            {
                var originalAmount = amount;
                if (noId)
                {

                    foreach (var asset in Gambler_Profiles[gambler.User.KfId].Assets.Values)
                    {
                        if (asset is Investment inv && inv.investment_type == InvestmentType.Stake)
                        {
                            value = inv.GetCurrentValue();
                            if (amount >= value)
                            {
                                Gambler_Profiles[gambler.User.KfId].Assets.Remove(assetId);
                                Gambler_Profiles[gambler.User.KfId].ModifyBalance(value);
                                amount -= value;
                                await BotInstance.SendChatMessageAsync(
                                    $"{gambler.User.FormatUsername()} unstaked {stake}. {await Gambler_Profiles[gambler.User.KfId].FormatBalanceAsync()}");
                            }
                            else
                            {
                                ((Investment)Gambler_Profiles[gambler.User.KfId].Assets[assetId]).StakePartialSale(amount);
                                stake = ((Investment)Gambler_Profiles[gambler.User.KfId].Assets[assetId]);
                                Gambler_Profiles[gambler.User.KfId].ModifyBalance(amount);
                                amount = 0;
                                await BotInstance.SendChatMessageAsync($"{gambler.User.FormatUsername()}, partially unstaked {stake}. {await Gambler_Profiles[gambler.User.KfId].FormatBalanceAsync()}", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
                                await BotInstance.SendChatMessageAsync($"{gambler.User.FormatUsername()}, successfully unstaked {await originalAmount.FormatKasinoCurrencyAsync()}", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
                                break;
                            }
                        }
                    }
                }
                else
                {
                    //partially unstake based on ID
                    ((Investment)Gambler_Profiles[gambler.User.KfId].Assets[assetId]).StakePartialSale(amount);
                    stake = ((Investment)Gambler_Profiles[gambler.User.KfId].Assets[assetId]);
                    Gambler_Profiles[gambler.User.KfId].ModifyBalance(amount);
                    await BotInstance.SendChatMessageAsync(
                        $"{gambler.User.FormatUsername()} partially unstaked {stake}. {await Gambler_Profiles[gambler.User.KfId].FormatBalanceAsync()}");
                }
            }
        }
        await SaveProfiles();
    }
    public async Task ProcessAssetSale(GamblerDbModel gambler, int assetId)
    {
        if (!Gambler_Profiles[gambler.User.KfId].Assets.ContainsKey(assetId))
        {
            await BotInstance.SendChatMessageAsync($"{gambler.User.FormatUsername()}, you don't have any assets with id {assetId}.", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
            return;
        }
        var asset = Gambler_Profiles[gambler.User.KfId].Assets[assetId];
        int cooldown;
        if (asset is Investment inv)
        {
            switch (inv.investment_type)
            {
                case InvestmentType.Gold or InvestmentType.Silver:
                    cooldown = (DateTime.UtcNow - inv.acquired).Days;
                    if (cooldown < 5)
                    {
                        await BotInstance.SendChatMessageAsync($"{gambler.User.FormatUsername()}, you can't sell your {inv.investment_type} investment yet, It's been less than 5 days since you bought it. {cooldown} days until it arrives.", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
                        return;
                    }

                    break;
                case InvestmentType.Stake:
                    cooldown = (DateTime.UtcNow - inv.acquired).Days;
                    if (cooldown < 7)
                    {
                        await BotInstance.SendChatMessageAsync($"{gambler.User.FormatUsername()}, you can't sell your Stake yet, {7-cooldown} days until it unlocks.", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
                        return;
                    }

                    break;
            }
        }
        else if (asset is Smashable smash)
        {
            await BotInstance.SendChatMessageAsync($"{gambler.User.FormatUsername()}, nobody wants to buy your shitty {smash}.", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
            return;
        }
        
        
        
        Gambler_Profiles[gambler.User.KfId].Assets.Remove(assetId);
        Gambler_Profiles[gambler.User.KfId].ModifyBalance(asset.GetCurrentValue());
        await BotInstance.SendChatMessageAsync(
            $"{gambler.User.FormatUsername()} sold {asset}. {await Gambler_Profiles[gambler.User.KfId].FormatBalanceAsync()}");
        await SaveProfiles();
    }
    

    public async Task ProcessSkinPurchase(GamblerDbModel gambler, int num)
    {
        //first confirm sufficient balance
        var skin = BotInstance.BotServices.KasinoShop!.Gambler_Profiles[gambler.User.KfId].sMarket.GetSkins(gambler)[num];
        if (BotInstance.BotServices.KasinoShop!.Gambler_Profiles[gambler.User.KfId].Balance()[1] < skin.originalValue)
        {
            await BotInstance.SendChatMessageAsync($"{gambler.User.FormatUsername()}, you don't have enough krypto to buy this skin. {await Gambler_Profiles[gambler.User.KfId].FormatBalanceAsync()}", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
            return;
        }
        BotInstance.BotServices.KasinoShop!.Gambler_Profiles[gambler.User.KfId].ModifyBalance(-skin.originalValue);
        BotInstance.BotServices.KasinoShop!.Gambler_Profiles[gambler.User.KfId].Assets.Add(skin.Id, skin);
        BotInstance.BotServices.KasinoShop!.Gambler_Profiles[gambler.User.KfId].sMarket.SellsSkinTo(gambler, num);
        await BotInstance.SendChatMessageAsync($"{gambler.User.FormatUsername()}, you bought {skin}. {await Gambler_Profiles[gambler.User.KfId].FormatBalanceAsync()}");
        await SaveProfiles();
    }
    public async Task PrintSkinMarket(GamblerDbModel gambler)
    {
        string str = $"{gambler.User.FormatUsername()}'s skins:[br]";
        var profile = Gambler_Profiles[gambler.User.KfId];
        var skins = profile.sMarket.GetSkins(gambler);
        for (int i = 0; i < skins.Count; i++)
        {
            str += $"{i + 1}: {skins[i]}[br]";
        }
        await BotInstance.SendChatMessageAsync(str, true, autoDeleteAfter: TimeSpan.FromSeconds(15));
    }


    public async Task PrintShoeMarket(GamblerDbModel gambler)
    {
        string str = "Shoes for sale:[br]";
        var shoeMarket = Gambler_Profiles[gambler.User.KfId].shMarket;
        int counter = 1;
        foreach (var shoe in shoeMarket.GetShoes(gambler))
        {
            str += $"{counter}: {shoe}[br]";
            counter++;
        }
        await BotInstance.SendChatMessageAsync(str, true, autoDeleteAfter: TimeSpan.FromSeconds(15));
    }
    public async Task ProcessShoePurchase(GamblerDbModel gambler, int num)
    {
        //yeezy adidas jordan
        
        var shoeMarket = Gambler_Profiles[gambler.User.KfId].shMarket;
        var shoe = shoeMarket.GetShoes(gambler)[num];
        
        if (Gambler_Profiles[gambler.User.KfId].Balance()[1] < shoe.originalValue)
        {
            await BotInstance.SendChatMessageAsync(
                $"{gambler.User.FormatUsername()}, you don't have enough krypto to buy {shoe}.[br] {await Gambler_Profiles[gambler.User.KfId].FormatBalanceAsync()}");
            return;
        }
        Gambler_Profiles[gambler.User.KfId].Assets.Add(shoe.Id, shoe);
        Gambler_Profiles[gambler.User.KfId].ModifyBalance(-shoe.originalValue);
    }
    public async Task UpdateGambler(GamblerDbModel gambler)
    {
        //if someone abandons their gambler profile they can do !shop update gambler to update their gambler profile
        Gambler_Profiles[gambler.User.KfId].GamblerId = gambler.Id;
        await SaveProfiles();
    }
    
    
    /*
     * STUFF TO IMPLEMENT IF THIS PROBLEM ACTUALLY HAPPENS, CURRENTLY UNIMPLEMENTED----------------------------------------------------------------
     */
    public async Task UpdateProfileId(GamblerDbModel gambler)
    {
        //if someone gets their account fucked with by null, assuming their gambler id stays the same
        foreach (var key in Gambler_Profiles.Keys)
        {
            if (Gambler_Profiles[key].GamblerId == gambler.Id)
            {
                Gambler_Profiles[key].ID = gambler.User.KfId;
            }
        }
        await SaveProfiles();
    }
    /*
     * -----------------------------------------------------------------------------------------------------------------------------------------
     */
    public async Task ProcessSmash(GamblerDbModel gambler)
    {
        await BotInstance.BotServices.KasinoShop!.Gambler_Profiles[gambler.User.KfId].Smash(gambler);
        
        await SaveProfiles();
    }

    public async Task PrintRtp(GamblerDbModel gambler)
    {
        var rtp = Gambler_Profiles[gambler.User.KfId].Tracker.GetRtp();
        await BotInstance.SendChatMessageAsync(rtp, true, autoDeleteAfter: TimeSpan.FromSeconds(10));
    }

    public async Task PrintAssets(GamblerDbModel gambler)
    {
        string str = $"{gambler.User.FormatUsername()}'s assets:[br]";
        int counter = 1;
        bool hasAssets = false;
        foreach (var asset in Gambler_Profiles[gambler.User.KfId].Assets.Values)
        {
            str += $"{counter}: {asset}[br]";
            counter++;
            hasAssets = true;
        }
        if (!hasAssets) str = $"{gambler.User.FormatUsername()}, you don't have any assets.";
        await BotInstance.SendChatMessageAsync(str, true, autoDeleteAfter: TimeSpan.FromSeconds(10));
    }

    public async Task PrintInvestments(GamblerDbModel gambler)
    {
        string str = $"{gambler.User.FormatUsername()}'s investments:[br]";
        int counter = 1;
        bool hasInvestments = false;
        foreach (var asset in Gambler_Profiles[gambler.User.KfId].Assets.Values)
        {
            if (asset is Investment i)
            {
                str += $"{counter}: {i}[br]";
                counter++;
                hasInvestments = true;
            }
        }
        if (!hasInvestments) str = $"{gambler.User.FormatUsername()}, you don't have any investments.";
        await BotInstance.SendChatMessageAsync(str, true, autoDeleteAfter: TimeSpan.FromSeconds(10));
    }
    
    public async Task CreateProfile(GamblerDbModel gambler)
    {
        await BotInstance.SendChatMessageAsync($"Creating profile for {gambler.User.FormatUsername()}...", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
        if (Gambler_Profiles.ContainsKey(gambler.User.KfId))
        {
            throw new Exception("Attempted to create a new profile for someone who seems to already have a profile?");
        }
        var profile = new KasinoShopProfile(gambler);
        Gambler_Profiles.Add(profile.ID, profile);
        await SaveProfiles();
        
        
    }










    
    
    
    
    
    
    
    
    
    
    
    public class KasinoShopProfile
    {
        public int ID { get; set; }
        public int GamblerId { get; set; }
        public string name;
        private decimal CryptoBalance;
        public decimal OutstandingLoanBalance;
        public Dictionary<int, Asset> Assets;
        public Dictionary<int, Loan> Loans = new();
        public decimal[] SponsorWagerLock = new decimal[2]; //[0] is how much you've wagered against your wager requirement, [1] is the wager requirement
        public decimal HouseEdgeModifier = 0;
        public int CrackCounter = 0;
        public int FloorNugs = 0;
        public DateTime LastSmokedCrack = DateTime.MinValue;
        public bool IsSponsored;
        public DateTime lastSponsorBonus = DateTime.MinValue;
        public bool IsWeeded;
        public bool IsCracked;
        public bool IsInWithdrawal;
        public bool IsLoanable;
        public SkinMarket sMarket;
        public ShoeMarket shMarket;
        private CancellationTokenSource CrackToken = new();
        private CancellationTokenSource WeedToken = new();
        private CancellationTokenSource BegToken = new();
        private TimeSpan WeedTimer = TimeSpan.FromSeconds(0); //time remaining on your weed buff
        private TimeSpan CrackTimer = TimeSpan.FromSeconds(0);
        public int KreditScore;
        public StatTracker Tracker;
        
        public KasinoShopProfile(GamblerDbModel gambler)
        {
            int gid = gambler.Id;
            int kfid  = gambler.User.KfId;
            ID = kfid;
            GamblerId = gid;
            Assets = new();
            CryptoBalance = 0;
            OutstandingLoanBalance = 0;
            IsSponsored = false;
            IsWeeded = false;
            IsCracked = false; 
            IsInWithdrawal = false;
            IsLoanable = false;
            KreditScore = 100;
            Tracker = new StatTracker(gid, kfid);
            name = gambler.User.FormatUsername();
            sMarket = new SkinMarket(gambler);
            shMarket = new ShoeMarket(gambler);

        }

        public async Task Beg(UserDbModel user)
        {
            CancellationToken bToken = BegToken.Token;
            IsLoanable = true;
            var msg = await BotInstance.SendChatMessageAsync($"{user.FormatUsername()}({user.KfId}) is begging for a loan. {user.FormatUsername()} can be trused with ${KreditScore} KKK in krypto with a 1.5x return.");
            int counter = 0;
            while (!bToken.IsCancellationRequested && counter < 100)
            {
                await Task.Delay(TimeSpan.FromSeconds(120), bToken);
                counter++;
            }
            
            
            await BotInstance.KfClient.EditMessageAsync(msg.ChatMessageUuid,
                $"{user.FormatUsername()}, nobody wanted to give you a loan. !beg to continue begging for a loan.");
            IsLoanable = false;
            await Task.Delay(TimeSpan.FromSeconds(10));
            await BotInstance.KfClient.DeleteMessageAsync(msg.ChatMessageUuid);
            
        }

        public bool SponsorBonusDue()
        {
            if (DateTime.UtcNow - lastSponsorBonus < TimeSpan.FromDays(1)) return false;
            return true;
        }

        public void ProcessSponsorBonus(decimal amount)
        {
            Tracker.AddDeposit(amount);
        }
        public void ModifyBalance(decimal amount)
        {
            CryptoBalance += amount;
        }
        public async Task<string> FormatBalanceAsync()
        {
            string str = OutstandingLoanBalance > 0 ? $"| Net Balance: {await (CryptoBalance-OutstandingLoanBalance).FormatKasinoCurrencyAsync()}":"";
            return $"Balance: {await CryptoBalance.FormatKasinoCurrencyAsync()}{str}";
        }
        public decimal[] Balance()
        {
            return new decimal[] {CryptoBalance, CryptoBalance - OutstandingLoanBalance};
        }
        public async Task SmokeCrack()
        {
            CancellationToken cToken = CrackToken.Token;
            if (IsCracked || IsInWithdrawal)
            {
                CrackToken.Cancel();
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
            LastSmokedCrack = DateTime.UtcNow;
            IsCracked = true;
            CrackTimer += TimeSpan.FromMinutes(2);
            CrackCounter++;
            HouseEdgeModifier += (decimal).05;

            for (int i = 0; i < CrackTimer.Seconds/5; i++)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                CrackTimer -= TimeSpan.FromSeconds(5);
                if (cToken.IsCancellationRequested)
                {
                    //if you smoked more crack within that 2 minutes, add another 2 minutes of crack instead, stack the buffs and postpone the withdrawal symptoms
                    return;
                }
            }
            //now you are in withdrawal
            IsInWithdrawal = true;
            IsCracked = false;
            
            HouseEdgeModifier -= (decimal)(.06 * CrackCounter);
            for (int i = 0; i < CrackCounter*100; i++)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                if (cToken.IsCancellationRequested)
                {
                    //if you smoke crack while in withdraw, get the basic benefits of crack back but do not reset crackcounter
                    HouseEdgeModifier = BotInstance.BotServices.KasinoShop!.DefaultHouseEdgeModifier;
                    return;
                }
            }
            //reset the house edge modifier and crack counter after withdrawal has passed
            CrackCounter = 0;
            HouseEdgeModifier = BotInstance.BotServices.KasinoShop!.DefaultHouseEdgeModifier;
            IsInWithdrawal = false;
        }

        public async Task SmokeWeed(TimeSpan buffLength)
        {
            if (buffLength > TimeSpan.FromMinutes(12)) FloorNugs++;
            CancellationToken wToken = WeedToken.Token;
            CancellationToken cToken = CrackToken.Token;
            if (IsWeeded)
            {
                WeedToken.Cancel();
                await Task.Delay(TimeSpan.FromSeconds(5)); 
            }
            IsWeeded = true;
            if (HouseEdgeModifier < 0)
            {
                //if you're currently in crack withdrawal
                HouseEdgeModifier /= 2;
            }
            else
            {
                HouseEdgeModifier += (decimal)0.01;
            }

            WeedTimer += buffLength;
            for (int i = 0; i < buffLength.Seconds / 5; i++)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
                WeedTimer -= TimeSpan.FromSeconds(1);
                if (wToken.IsCancellationRequested) return;
                if (cToken.IsCancellationRequested) return;
                
            }
            await Task.Delay(buffLength);
            if (HouseEdgeModifier > 0) HouseEdgeModifier -= (decimal)0.01;
            IsWeeded = false;

        }

        public async Task Smash(GamblerDbModel gambler)
        {
            List<int> smashableAssetIds = new();
            bool smashableAssets = false;
            foreach (var key in Assets.Keys)
            {
                if (Assets[key] is Smashable smash)
                {
                    smashableAssetIds.Add(key);
                    smashableAssets = true;
                }
            }
            if (!smashableAssets)
            {
                //check if they have any physical assets in their possesion, shoes, gold, silver, house, car, small chance to damage one of those instead
                List<int> physicalAssetIds = new();
                foreach (var key in Assets.Keys)
                {
                    if (Assets[key] is Investment inv)
                    {
                        if (inv.investment_type != InvestmentType.Stake && inv.investment_type != InvestmentType.Skin)
                        {
                            physicalAssetIds.Add(key);
                        }
                    }
                    else if (Assets[key] is Shoe shoe)
                    {
                        physicalAssetIds.Add(key);
                    }
                    else if (Assets[key] is Car car)
                    {
                        physicalAssetIds.Add(key);
                    }
                }

                int num = Money.GetRandomNumber(gambler, 0, 100);
                if (physicalAssetIds.Count == 0 || num < 90)
                {
                    await BotInstance.SendChatMessageAsync($"{gambler.User.FormatUsername()}, you don't have anything to smash.", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
                    return;
                }
                
                //now "smash" one of the assets
                num = Money.GetRandomNumber(gambler, 0, 4);
                var assetId = smashableAssetIds[num];
                if (Assets[assetId] is Shoe sh)
                {
                    sh.Smash();
                }
                else if (Assets[assetId] is Car car)
                {
                    car.Smash();
                }
                else if (Assets[assetId] is Investment inv)
                {
                    inv.Smash();
                }
                await BotInstance.SendChatMessageAsync($"{gambler.User.FormatUsername()}, you smashed {Assets[assetId]}.", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
                foreach (var lKey in Loans.Keys)
                {
                    Loans[lKey].ProcessSmash(ID, BotInstance);
                }

                return;
            }
            foreach (var id in smashableAssetIds)
            {
                ((Smashable)Assets[id]).Smash();
                await BotInstance.SendChatMessageAsync($"{gambler.User.FormatUsername()}, you smashed {Assets[id]}.");
                if (Assets[id].GetCurrentValue() == 0)
                {
                    Assets.Remove(id);
                    await BotInstance.SendChatMessageAsync($"{gambler.User.FormatUsername()}, {Assets[id]} is worthless now so you throw it away.");
                }
                if (Money.GetRandomNumber(gambler, 0, 100) < 50) break;
            }

            foreach (var lKey in Loans.Keys)
            {
                Loans[lKey].ProcessSmash(ID, BotInstance);
            }
        }
        public void Withdraw(decimal amount)
        {
            CryptoBalance += amount;
            Tracker.AddWithdrawal(amount);
        }

        public void Deposit(decimal amount)
        {
            CryptoBalance -= amount;
            Tracker.AddDeposit(amount);
        }
        
        
        

        
        
        
        
        
        
        public class StatTracker
        {
            public int GamblerId;
            public int KfId;
            public decimal totalDeposited = 0;
            public decimal totalWithdrawn = 0;
            public decimal totalLossBack = 0;
            public Dictionary<WagerGame, decimal[]> totalWageredByGame; //0 is total wagered, 1 is total paid back 
            
            public StatTracker(int gid, int kfid)
            {
                GamblerId = gid;
                KfId = kfid;
                totalWageredByGame = new Dictionary<WagerGame, decimal[]>();
                foreach (var game in Enum.GetValues<WagerGame>())
                {
                    totalWageredByGame.Add(game, new decimal[] {0, 0});
                }
            }
            
            public void AddNewGameToTracker(WagerGame game)
            {
                totalWageredByGame.Add(game, new decimal[] {0, 0});
            }

            public void AddWager(WagerGame game, decimal amount, decimal net)
            {
                if (!totalWageredByGame.ContainsKey(game)) AddNewGameToTracker(game);
                totalWageredByGame[game][0] += amount;
                totalWageredByGame[game][1] += net;
            }

            public void AddDeposit(decimal amount)
            {
                totalDeposited += amount;
            }
            public void AddWithdrawal(decimal amount)
            {
                totalWithdrawn += amount;
            }

            public void AddLossback(decimal amount)
            {
                totalLossBack += amount;
            }

            public string GetRtp()
            {
                decimal totalWagered = 0;
                decimal totalWinnings = 0;
                foreach (var wagered in totalWageredByGame.Values)
                {
                    totalWagered += wagered[0];
                    totalWinnings += wagered[1];
                }

                using var db = new ApplicationDbContext();
                var gambler = db.Gamblers.FirstOrDefaultAsync(g => g.Id == GamblerId).GetAwaiter().GetResult();
                decimal RTP = (totalWagered - totalWithdrawn - gambler!.Balance) / totalWagered;
                string returnVal = $"{gambler.User.FormatUsername()}[br]" +
                                   $"Global RTP: {RTP * 100}%[br]";
                foreach (var game in totalWageredByGame.Keys)
                {
                    returnVal += $"{game} RTP: {(totalWageredByGame[game][1] / totalWageredByGame[game][0]) * 100}%[br]";
                }

                return returnVal;
            }
        }
        
    }
    public abstract class Asset
    {
        public decimal originalValue;
        public string name;
        public AssetType type;
        public DateTime acquired;
        public List<AssetValueChangeReport> ValueChangeReports;
        public int Id;
        
        public abstract decimal GetCurrentValue();
    }

    public class AssetValueChangeReport
    {
        private decimal valueChangeAmount;
        private decimal valueChangePcnt;
        public DateTime time;
        
        public AssetValueChangeReport(decimal valueChangeAmount, decimal valueChangePcnt, DateTime time)
        {
            this.valueChangeAmount = valueChangeAmount;
            this.valueChangePcnt = valueChangePcnt;
            this.time = time;
        }

        public override string ToString()
        {
            string symbol = valueChangeAmount > 0 ? AssetValueIncreaseIndicator[true] : AssetValueIncreaseIndicator[false];
            string color = valueChangeAmount > 0 ? AssetValueIncreaseColor[true] : AssetValueIncreaseColor[false];
            string timeString = DateTime.UtcNow - time > TimeSpan.FromDays(7) ? time.ToString("g") : time.ToString("f");
            return $"{symbol}[color={color}]${valueChangeAmount} KKK({valueChangePcnt}%)[/color] {timeString}";
        }
    }

    public class Loan
    {
        public decimal amount;
        public decimal payoutAmount;
        public int payableToGambler; //gambler id
        public int payableToKf;//kfid
        public int recieverGambler;
        public int recieverKf;
        public int Id;
        public string payableTo;

        public Loan(decimal amount, int payableToGambler, int payableToKf, int recieverGambler, int recieverKf, int Id, UserDbModel payableTo)
        {
            this.amount = amount;
            payoutAmount = amount * 1.5m;
            this.payableToGambler = payableToGambler;
            this.payableToKf = payableToKf;
            this.recieverGambler = recieverGambler;
            this.recieverKf = recieverKf;
            this.Id = Id;
            this.payableTo = payableTo.FormatUsername();
        }

        public async Task<string> ToStringAsync(int kfId)
        {
            if (kfId == payableToKf) //if the person calling the command (ex !list loans) is the one who is owed
            {
                await using var db = new ApplicationDbContext();
                var gambler = await db.Gamblers.FirstOrDefaultAsync(g => g.Id == payableToGambler);
                return $"is owed ${await payoutAmount.FormatKasinoCurrencyAsync()} from {gambler!.User.FormatUsername()}";
            }
                
            return $"owes {payableTo}({payableToKf}) ${payoutAmount}KKK";
        }

        public void ProcessSmash(int smasherId, ChatBot instance)
        {
            if (smasherId == recieverKf)
            {
                if (payoutAmount > amount)
                {
                    payoutAmount = amount;
                    instance.BotServices.KasinoShop!.Gambler_Profiles[payableToKf].Loans[Id].payoutAmount = amount;
                }
            }
        }
        
        [Obsolete("Don't use base ToString, use await ToStringAsync(int kfId) instead", true)]
        public override string ToString()
        {
            return "Generated incorrect string for loan. Screenshot and send to Alogindtractor2";
        }
    }

        
        
    public class Investment : Asset //gold, silver, stake, or house
    {
        private decimal _currentValue;
        public InvestmentType investment_type;
        private DateTime _lastInterestCalculation = DateTime.UtcNow;
        public decimal[] interestRange;

        [Obsolete("Dont use base constructor", true)]
        protected Investment()
        {
            throw new Exception("Investment should not be instantiated directly. Use Gold, Silver, or House");
        }
        public Investment(int id, decimal value, decimal[] range, InvestmentType type, string name)
        {
             originalValue = value;
             _currentValue = value;
             investment_type = type;
             interestRange = range;
             Id = id;
             this.type = AssetType.Investment;
             this.name = name;
             ValueChangeReports = new();
             ValueChangeReports.Add(new AssetValueChangeReport(0, 0, DateTime.UtcNow));
        }

        public override decimal GetCurrentValue()
        {
            //apply daily interest if applicable
            if (DateTime.UtcNow - _lastInterestCalculation > TimeSpan.FromDays(1))
            {
                int interestIterations = (DateTime.UtcNow - acquired).Days;
                for (int i = 0; i < interestIterations; i++)
                {
                    
                    double range = (double)(interestRange[1] - interestRange[0]);
                    double random = _rand.NextDouble() * range;
                    random += (double)interestRange[0];
                    var oldValue = _currentValue;
                    _currentValue *= (decimal)(1 + random);
                    ValueChangeReports.Add(new AssetValueChangeReport(_currentValue - oldValue, (decimal)(random), DateTime.UtcNow - TimeSpan.FromDays(i+1)));
                }
            }
            return _currentValue;
        }
        public override string ToString()
        {
            return $"{type} {investment_type}: {name}(ID: {Id}) worth {GetCurrentValue()} {ValueChangeReports[^1]}";
        }

        public void StakePartialSale(decimal amount)
        {
            if (this.investment_type != InvestmentType.Stake) throw new Exception("attempted to partially sell something other than a stake");
            var oldval = _currentValue;
            _currentValue -= amount;
            ValueChangeReports.Add(new AssetValueChangeReport(-amount, -(_currentValue/oldval), DateTime.UtcNow));
        }

        public void Smash()
        {
            if (investment_type == InvestmentType.Stake || investment_type == InvestmentType.Skin) throw new Exception("attempted to smash a stake");
            var oldval = _currentValue;
            _currentValue -= originalValue/2;
            ValueChangeReports.Add(new AssetValueChangeReport(_currentValue - oldval, (_currentValue - oldval) / oldval, DateTime.UtcNow));
        }
    }

    public class Shoe : Investment
    {
        public ShoeBrand brand;
        public Shoe(int id, decimal value, ShoeBrand brand) : base(id, value, ShoeAprRange, InvestmentType.Shoes, $"{brand} shoes")
        {
            this.brand = brand;
        }
    }

    public class Skin : Investment
    {
        private decimal _currentValue;
        private DateTime _lastInterestCalculation = DateTime.UtcNow;
        private string _objName;
        private string _tag;
        private string _color;
        private string _emoji;

        public Skin(int id, decimal value, string obj, string tag, string color, string emoji) : base(id, value, CsSkinAprRange, InvestmentType.Skin, $"[color={color}][b][{emoji}{tag}]{obj}[/b][/color]")
        {
            _objName = obj;
            _tag = tag;
            _color = color;
            _emoji = emoji;
        }
        
        //use same getCurrentValue as investment
        
        public override string ToString()
        {
            return $"{name} (ID: {Id}) worth ${GetCurrentValue()} KKK | {ValueChangeReports[^1]}";
        }
    }

    public class Smashable : Asset // computer equipment
    {
        public bool isSmashed = false;
        public decimal currentValue;
        
        public Smashable(int id, decimal value, SmashableType type)
        {
            Id = id;
            originalValue = value;
            currentValue = value;
            this.type = AssetType.Smashable;
            this.name = $"{type}";
            acquired = DateTime.UtcNow;
            ValueChangeReports = new();
        }

        public override decimal GetCurrentValue()
        {
            return currentValue;
        }
        
        public void Smash()
        {
            isSmashed = true;
            currentValue -= originalValue / 2;
            name = $"Smashed {name}";
            ValueChangeReports.Add(new AssetValueChangeReport(-originalValue/2, -.5m, DateTime.UtcNow));
        }
        
        public override string ToString()
        {
            return $"{name}(ID: {Id}) worth {currentValue} {ValueChangeReports[^1]}";
        }
    }

    
    public class Car : Asset
    {
        private decimal _currentValue;
        public new AssetType type = AssetType.Car;
        public Cars car_type;
        public decimal job_value;
        
        [Obsolete("Dont use base constructor", true)]
        public Car()
        {
            throw new Exception("Car should not be instantiated directly. Use Car(int id, string name, decimal value, Cars car_type, decimal job_value)");
        }
        public Car(Cars type)
        {
            acquired = DateTime.UtcNow;
            car_type = type;
            _currentValue = CarPrices[type];
            originalValue = _currentValue;
            job_value = CarPrices[type] / 10;
            name = type.ToString();
            ValueChangeReports = new();
        }

        public override decimal GetCurrentValue()
        {
            return _currentValue;
        }

        public void SetId(GamblerDbModel gambler) //sets the id of the car to a unique number when you buy it so you can interact with it later 
        {
            Id = GenerateRandomId(gambler);
        }

        public async Task ProcessWorkJob(GamblerDbModel gambler)
        {
            decimal oldVal = _currentValue;
            _currentValue -= job_value / 5;
            ValueChangeReports.Add(new AssetValueChangeReport(-job_value / 5, (_currentValue - oldVal) / oldVal, DateTime.UtcNow));
            BotInstance.BotServices.KasinoShop!.Gambler_Profiles[gambler.User.KfId].ModifyBalance(job_value);
            if (_currentValue <= 0) // if your car dies it gets removed from your asset list
            {
                BotInstance.BotServices.KasinoShop!.Gambler_Profiles[gambler.User.KfId].Assets.Remove(Id);
                await BotInstance.SendChatMessageAsync(
                    $"{gambler.User.FormatUsername()} totalled their car on the way home from work.", true,
                    autoDeleteAfter: TimeSpan.FromSeconds(10));
            }
            
        }
        
        public void Smash() //car can be damaged as a result of smashing but not destroyed
        {
            var oldval = _currentValue;
            _currentValue -= _currentValue / 4;
            ValueChangeReports.Add(new AssetValueChangeReport(_currentValue - oldval, (_currentValue - oldval) / oldval, DateTime.UtcNow));
        }
        
        public override string ToString()
        {
            return $"{type} {name} worth ${GetCurrentValue()} KKK";
        }
        
    }
    
    
    //KasinoShop.[]Market - used to generate a list of items for you to buy when you interact with the shop. takes your shop profiles current state into account
    //for example if you have a car, you can take meth queen home with you and buy meth
    //markets get new options every day except drug market which stays the same outside of prices
    

    public class ShoeMarket
    {
        private List<Shoe> _shoes = new();
        private DateTime _opened = DateTime.UtcNow;

        public ShoeMarket(GamblerDbModel gambler)
        {
            var shoePrices = KasinoShop.ShoePrices(gambler);
            foreach (var shoeType in shoePrices.Keys)
            {
                _shoes.Add(new Shoe(GenerateRandomId(gambler), shoePrices[shoeType], shoeType));
            }
        }

        public bool Old()
        {
            if (DateTime.UtcNow - _opened > TimeSpan.FromDays(1)) return true;
            return false;
        }
        
        public List<Shoe> GetShoes(GamblerDbModel gambler)
        {
            if (Old())
            {
                var shoePrices = KasinoShop.ShoePrices(gambler);
                _shoes.Clear();
                foreach (var shoeType in shoePrices.Keys)
                {
                    _shoes.Add(new Shoe(GenerateRandomId(gambler), shoePrices[shoeType], shoeType));
                }
            }

            return _shoes;
        }
        
    }
    
    public class SkinMarket
    {
        private List<Skin> _skins = new();
        private DateTime _opened = DateTime.UtcNow;

        public SkinMarket(GamblerDbModel gambler)
        {
            for (int i = 0; i < 5; i++)
            {
                _skins.Add(GenerateRandomSkin(gambler));
            }
        }
        private bool Old()
        {
            return DateTime.UtcNow - _opened > TimeSpan.FromDays(1);
        }
        
        public List<Skin> GetSkins(GamblerDbModel gambler)
        {
            if (Old())
            {
                _skins.Clear();
                for (int i = 0; i < 5; i++)
                {
                    _skins.Add(GenerateRandomSkin(gambler));
                }
            }

            return _skins;
        }

        public void SellsSkinTo(GamblerDbModel gambler, int skindex)
        {
            _skins.RemoveAt(skindex);
            _skins.Add(GenerateRandomSkin(gambler));
        }
    }
    

    public static readonly decimal CrackPrice = 10000m;
    public static readonly decimal WeedPricePerHour = 1000m;
    public static readonly TimeSpan WeedNugLength = TimeSpan.FromMinutes(6);
    public static readonly decimal CsSkinMinBaseValue = 1000;
    public static readonly decimal[] ShoeAprRange = { -0.05m, 0.05m };
    public static readonly decimal[] CsSkinAprRange = { -0.25m, 0.25m };
    public static decimal HomeApr = 0.1m;

    public static Skin GenerateRandomSkin(GamblerDbModel gambler)
    {
        int obj = Money.GetRandomNumber(gambler, 0, CsSkinObjects.Count - 1);
        int tg = Money.GetRandomNumber(gambler, 0, CsSkinTags.Count - 1);
        int emo = Money.GetRandomNumber(gambler, 0, CsSkinEmotes.Count - 1);
        string color = GetRandomColor(gambler);
        int id = GenerateRandomId(gambler);
        decimal val = CsSkinTags.ElementAt(tg).Value + CsSkinEmotes.ElementAt(emo).Value;
        if (val < CsSkinMinBaseValue) val = CsSkinMinBaseValue;
        return new Skin(id, val, CsSkinObjects[obj], CsSkinTags.ElementAt(tg).Key, color, CsSkinEmotes.ElementAt(emo).Key);
    }


    public static int GenerateRandomId(GamblerDbModel gambler)
    {
        var profile = BotInstance.BotServices.KasinoShop!.Gambler_Profiles[gambler.User.KfId];
        int counter = 0;
        int id = Money.GetRandomNumber(gambler, 0, 999999999);
        if (profile.Assets.ContainsKey(id))
        {
            while (profile.Assets.ContainsKey(id))
            {
                id = Money.GetRandomNumber(gambler, 0, 999999999);
                counter++;
                if (counter > 10000)
                {
                    throw new Exception("failed to generate unique skin ID after 10000 attempts");
                }
            }
        }

        return id;
    }
    
    public static readonly Dictionary<Cars, Car> DefaultCars = new()
    {
        {Cars.Civic, new Car(Cars.Civic)},
        {Cars.Audi, new Car(Cars.Audi)},
        {Cars.Bentley, new Car(Cars.Bentley)},
        {Cars.Bmw, new Car(Cars.Bmw)}
    };
    public static readonly Dictionary<Cars, decimal> CarPrices = new()
    {
        { Cars.Civic , 2_000_000 },
        { Cars.Audi, 4_000_000 },
        { Cars.Bentley , 6_000_000 },
        { Cars.Bmw , 8_000_000 },
    };

    public static readonly List<string> CsSkinObjects = new()
    {
        "P2000",
        "USP-S",
        "Glock-18",
        "Dual Berettas",
        "P250",
        "Tec-9",
        "Five-SeveN",
        "CZ75-Auto",
        "R8 Revolver",
        "Desert Eagle",
        "Mac-10",
        "UMP-45",
        "MP5 SD",
        "MP7",
        "MP9",
        "P90",
        "PP-Bizon",
        "Nova",
        "Sawed-Off",
        "Mag-7",
        "XM1014",
        "SSG 08",
        "Galil AR",
        "FAMAS",
        "AK-47",
        "M4A1-S",
        "SG 553",
        "M4A4",
        "AUG",
        "AWP",
        "G3SG1",
        "SCAR-20",
        "Bayonet",
        "Bowie Knife",
        "Butterfly Knife",
        "Falchion Knife",
        "Flip Knife",
        "Gut Knife",
        "Huntsman Knife",
        "Karambit",
        "M9 Bayonet",
        "Navaja Knife",
        "Nomad Knife",
        "Paracord Knife",
        "Shadow Daggers",
        "Skeleton Knife",
        "Stiletto Knife",
        "Survival Knife",
        "Talon Knife",
        "Ursus Knife",
        "Kukri Knife",
        "Sport Gloves",
        "Specialist Gloves",
        "Driver Gloves",
        "Hand Wraps",
        "Moto Gloves",
        "Bloodhound Gloves",
        "Hydra Gloves",
        "Broken Fang Gloves"
    };

    public static readonly Dictionary<string, decimal> CsSkinTags = new()
    {
        {"SNEED", 10000000},
        {"R", 9000000},
        {"RIGGED", 8000000},
        {"GREEDY", 100000},
        {"DEWISH", 6000000},
        {"JEWISH", 6000000},
        {"SCAMMER", 7000000},
        {"SCAM", 7777777},
        {"5", 5555555},
        {"9", 9999999},
        {"OSRS", 1000},
        {"666", 6666666},
        {"CRACK", 5000000},
        {"WEED", 4200000},
        {"EEEEEEEEEE", 3333333},
        {"COFFEE", 3000000},
        {"FATGO", 2000000},
        {"YO", 75000},
        {"ELF", 60000},
        {"OMFG", 20000},
        {"IHML", 7000},
        {"FUCKIN DEWD", 60000},
        {"MILF", 40000},
        {"KKK", 1000000},
        {"KASINO", 1000000},
        {"METH", 4000000},
        {"GOOBR", 2000000},
        {"TRAPPER", 500000},
        {"TRAPPERTURD", 100000},
        {"CHRISTMAS", 10000},
        {"MR.CHRISTMAS", 10000},
        {"DEPAKOTE", 1000},
        {"RAT", 50000},
        {"MATI", 20000},
        {"GERMAN RAP", 15000},
        {"EVIL EDDIE", 10000},
        {"NASTY NOAH", 5000},
        {"BOSSMAN", 25000},
        {"RATDAD", 80000},
        {"PICKLETIME", -1000000m},
        
    };


    public static string GetRandomColor(GamblerDbModel gambler)
    {
        int r = Money.GetRandomNumber(gambler, 0, 255);
        int g = Money.GetRandomNumber(gambler, 0, 255);
        int b = Money.GetRandomNumber(gambler, 0, 255);
        return $"{r:X2}{g:X2}{b:X2}";
    }

    public static readonly Dictionary<string, decimal> CsSkinEmotes = new()
    {
        {"🤣", 15000},
        {"😂", 14000},
        {"🙂", 13000},
        {"😝", 12000},
        {"🤨", 11000},
        {"🙄", 10000},
        {"🤥", 20000},
        {"🤧", 30000},
        {"🤯", 8000000},
        {"😭", 30000},
        {"😤", 40000},
        {"💩", 60000},
        {"💢", 700000},
        {"💥", 600000},
        {"💤", 10000},
        {"🫵", 70000},
        {"🙏", 50000},
        {"🎅", 10000},
        {"🐀", 10000000},
        {"🐹", 220000},
        {"🦨", 110000},
        {"🐦‍🔥", 330000},
        {"🐍", 88000},
        {"🐉", 77000},
        {"🦞", 66000},
        {"☘", 300000},
        {"🍀", 1000000},
        {"🥩", 400000},
        {"🚔", 500000},
        {"🚗", 50000},
        {"✈", 1000000},
        {":winner:", 4000000},
        {":juice:", 2000000},
        {":ross:", 3000000},
        {"♠", 75000},
        {"♥", 75000},
        {"♦", 75000},
        {"♣", 75000},
        {"💎", 150000},
        {"🪫", 85000},
        {"🪙", 45000},
        {"💵", 10000},
        {"🧲", 25000},
        {"💊", 100000},
        {"🚬", 650000},
        {"🪪", 750000},
        {"🚭", 820000},
        {"✡", 6666666},
        {"❓", 250000},
        {"💲", 500000},
        {"🆗", 360000},
        {"🤡", 1000000},
        {"👅", 9999999},
        {"👴", 105000},
        {"🛹", 90000},
        {"🥒", -1000000m},
        {"🎄", 42069},
        {"🕹", 12000},
        {"🎰", 7777777},
        
    };

    

    public static readonly Dictionary<bool, string> AssetValueIncreaseIndicator = new() { {false, "🔻"}, {true, "🔺"} };
    public static readonly Dictionary<bool, string> AssetValueIncreaseColor = new() { {false, "red"}, {true, "lightgreen"} };
    
    public static Dictionary<ShoeBrand, decimal> ShoePrices(GamblerDbModel gambler)
    {
        
        return new Dictionary<ShoeBrand, decimal>
        {
            { ShoeBrand.Yeezy , Money.GetRandomNumber(gambler, 6_000, 100_000) }, 
            { ShoeBrand.Adidas , Money.GetRandomNumber(gambler, 1_800, 50_000) },
            { ShoeBrand.Jordan , Money.GetRandomNumber(gambler, 9_000, 380_000) },
        };
    }

    private static List<string> SmashCarousel = new()
    {
        "https://i.ddos.lgbt/u/KhMr9v.webp",
        "https://i.ddos.lgbt/u/KYaOqH.webp",
        "https://i.ddos.lgbt/u/w3wAyB.webp",
        "https://i.ddos.lgbt/u/c4znnv.webp",
        "https://i.ddos.lgbt/u/qGHbNp.webp",
        "https://i.ddos.lgbt/u/65lz4m.webp",
        "https://i.ddos.lgbt/u/ZCDWeO.webp",
        "https://i.ddos.lgbt/u/2025-12-12_19:17:16.gif",
        "https://i.ddos.lgbt/u/oBXBV4.webp",
        "https://i.ddos.lgbt/u/2025-12-12_19:08:15.gif",
        "https://i.ddos.lgbt/u/fuxIHW.webp",
        "https://i.ddos.lgbt/u/0dtwl3.webp",
        
    };

    public static readonly decimal GoldBasePriceOz = 300000;
    public static readonly decimal[] GoldInterestRange = new decimal[] { 0.01m, 0.05m };
    public static readonly decimal SilverBasePriceOz = 10000;
    public static readonly decimal[] SilverInterestRange = new decimal[] { 0.01m, 0.05m };
    public static readonly decimal BaseHousePrice = 100000000;
    public static readonly decimal[] HouseInterestRange = new decimal[] { 0.01m, 0.15m };
    public static readonly decimal[] CryptoStakeInterestRange = new decimal[] { -0.01m, 0.05m };
    
    public static String GetRandomSmashImage(GamblerDbModel gambler)
    {
        int rand = Money.GetRandomNumber(gambler, 0, SmashCarousel.Count - 1);
        return SmashCarousel[rand];
    }

    public decimal KASINO_SPONSOR_BONUS = 1000;
}





public enum SmashableType
{
    Headphones,
    Keyboard,
    Mouse
}
public enum InvestmentType
{
    Shoes,
    Stake,
    Gold,
    Silver,
    Skin,
    House,
    Random
}

public enum AssetType
{
    Investment,
    Smashable,
    Car,
    Random
}

public enum ShoeBrand
{
    Yeezy,
    Adidas,
    Jordan,
}

public enum Cars
{
    Civic,
    Bentley,
    Audi,
    Bmw
}

public enum Rigging
{
    Switch,
    Button,
    Lever,
    Panel,
    Keypad,
    Dial,
    Electromagnet
}



