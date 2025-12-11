using System.Text.RegularExpressions;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient.Models.Events;

namespace KfChatDotNetBot.Commands.Kasino;

[KasinoCommand]
[WagerCommand]
public class LambchopCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"lambchop (?<amount>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"lambchop (?<amount>\d+\.\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"lambchop (?<amount>\d+) (?<targetTile>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"lambchop (?<amount>\d+\.\d+) (?<targetTile>\d+)$", RegexOptions.IgnoreCase)
    ];

    public string? HelpText =>
        "Tread treacherous terrain towards terrific treasures. Play using !lambchop bet, amount of tiles you want to move";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(12);
    public RateLimitOptionsModel? RateLimitOptions => new()
    {
        MaxInvocations = 3,
        Window = TimeSpan.FromSeconds(15)
    };
    private static double _houseEdge = 0.015; // house edge hack?
    
    // game assets
    private const string HAIRSPACE = " ";
    private const string SHEEP = "🐑";
    private const string YELLOW_TILE = "🟡";
    private const string PURPLE_TILE = "🟣";
    private const string GREEN_TILE = "🟢";
    private const string RED_TILE = "🔴";
    private const string FORREST_TILE = "🌳";
    private const string DESERT_TILE = "🏜️";
    private const string WOLF = "🐺";
    private const string ALIEN = "🛸";
    private const string LIGHTNING = "⚡";
    private const string BLOOD = HAIRSPACE + "🩸" + HAIRSPACE;
    private const string SKULL = "☠";
    private const string MEDAL = "🏅";
    private const string MONEYBAG = "💰";
    private const string CELEBRATION = "🏆🪩✨";
    private const string CASTLE = "🏯";
    private const string WOOSH = "💨";
    private const string FIST = HAIRSPACE + "✊" + HAIRSPACE;
    private const string TILE_SPACING = "[color=#36393f]......[/color]";
    private const string HAZARD_SPACING = "[color=#36393f].......[/color]";
    // game settings
    private const int FRAME_DELAY = 200;    // time between lambchop frames in milliseconds
    private const int FIELD_LENGTH = 16;    // indicates how many tiles the lamb can cross. default is 16
                                            // WARNING: do NOT change without first implementing dynamic payout logic in LambchopPayoutMultiplier() 
                                            // has to be an EVEN number > 1
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromMilliseconds((await SettingsProvider.GetValueAsync(BuiltIn.Keys.KasinoLambchopCleanupDelay)).ToType<int>());
        if (!arguments.TryGetValue("amount", out var amount))
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, not enough arguments. !lambchop <wager> <number between 1 and {FIELD_LENGTH}>", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        var targetTile = arguments["targetTile"].Success ? Convert.ToInt32(arguments["targetTile"].Value) : FIELD_LENGTH;
        if (targetTile is < 1 or > FIELD_LENGTH)
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()},  Please choose a target tile between 1 and {FIELD_LENGTH}", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        var wager = Convert.ToDecimal(amount.Value);
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        if (gambler.Balance < wager)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, your balance of {await gambler.Balance.FormatKasinoCurrencyAsync()} isn't enough for this wager.",
                true, autoDeleteAfter: cleanupDelay);
            return;
        }
        var colors =
            await SettingsProvider.GetMultipleValuesAsync([
                BuiltIn.Keys.KiwiFarmsGreenColor, BuiltIn.Keys.KiwiFarmsRedColor
            ]);
       
        List<string> tiles = Enumerable.Repeat(YELLOW_TILE, FIELD_LENGTH / 2).ToList();
        tiles.AddRange(Enumerable.Repeat(PURPLE_TILE, FIELD_LENGTH / 2));
        List<string> hazards = Enumerable.Repeat(FORREST_TILE, FIELD_LENGTH / 2).ToList();
        hazards.AddRange(Enumerable.Repeat(DESERT_TILE, FIELD_LENGTH / 2));
        
        // calculate death tile, death tile = -1 means no death tile
        int deathTile = CalculateDeathTile(targetTile, gambler);
        bool win;
        int steps;
        if (deathTile == -1) // no death tile on field
        {
            win = true; // if there's is no deathTile then automatic win!
            steps = targetTile - 1;
        }
        else
        {
            win = (targetTile - 1) < deathTile; // if your targetTile is less then the death tile then you win!
            steps = win ? targetTile - 1 : deathTile;
        }
        // first game state
        var lambChopDisplayMessage =
            await botInstance.SendChatMessageAsync(ConvertLambchopFieldToString(tiles, hazards, true), true,
                autoDeleteAfter: cleanupDelay);
        while (lambChopDisplayMessage.ChatMessageId == null)
        {
            await Task.Delay(50, ctx); // wait until first message is fully sent
            if (lambChopDisplayMessage.Status is SentMessageTrackerStatus.Lost or SentMessageTrackerStatus.NotSending)
                return;
        }

        for (int i = -1; i <= steps;)       // main game loop, if/else "state machine"
        {
            if (i == -1)
            {
                // first state, print empty tileset and sheep placeholder
                await Task.Delay(TimeSpan.FromMilliseconds(FRAME_DELAY), ctx);
                tiles = MoveSheep(tiles); // move the sheep onto the first tile
                i++; // increase step counter by 1
                continue;
            }
            // boundary check for sheep movement
            if (i >= tiles.Count)
            {
                break; // exit if we've gone past the field
            }
            // normal "move" state

            // let alien follow player
            if (i > FIELD_LENGTH / 2 - 1)
            {
                hazards[i] = ALIEN; // alien follows you in later part of the map
                if (i > 0 && hazards[i - 1] == ALIEN)
                {
                    // update previous hazard tile back to desert
                    hazards[i - 1] = DESERT_TILE;
                }
               
            }
            if (i == deathTile) // trigger hazard death?
            {
                // player dies on this step
                if (i > FIELD_LENGTH / 2 - 1)
                {
                    // death by alien
                    await UpdateGameAsync();
                    tiles[i] = LIGHTNING;   // strike player with lightning
                    await UpdateGameAsync();
                    tiles[i] = SKULL;   // skull
                    await UpdateGameAsync();
                    break;
                    // i++;
                    //continue;
                }
                else
                {
                    // death by wolf
                    await UpdateGameAsync();
                    hazards[i] = WOLF;  // add wolf
                    await UpdateGameAsync();
                    tiles[i] = BLOOD;   // blood
                    await UpdateGameAsync();
                    tiles[i] = SKULL;   // skull
                    await UpdateGameAsync();
                    break;
                    //i++;
                    //continue;
                }
            }
            if (i == (targetTile - 1) && win) // trigger win animation
            {
                await UpdateGameAsync();                //arrive at targetTile
                if (targetTile == FIELD_LENGTH)
                {
                    // mega win, end of the line
                    string lambChopFieldEndState = ConvertLambchopFieldToString(tiles, hazards, false);
                    lambChopFieldEndState = lambChopFieldEndState.Replace(SHEEP, GREEN_TILE);
                    lambChopFieldEndState += SHEEP;
                    await UpdateGameAsync(lambChopFieldEndState);
                    lambChopFieldEndState += CELEBRATION;
                    await UpdateGameAsync(lambChopFieldEndState);
                    break;
                    //i++;
                    //continue;
                }
                if (i > FIELD_LENGTH / 2 - 1)
                {
                    // win in the tundra, moneybags
                    hazards[i] = MONEYBAG; // add moneybag
                    if (deathTile >= 0 && deathTile < tiles.Count)
                    {
                        tiles[deathTile] = RED_TILE; // add deathTile indicator
                    }
                    await UpdateGameAsync();
                    break;
                    //i++;
                    //continue;
                }
                else
                {
                    // win in the forrest, medal
                    hazards[i] = MEDAL; // add medal
                    if (deathTile != -1 && deathTile < tiles.Count)
                    {
                        tiles[deathTile] = RED_TILE; // add deathTile indicator
                    }
                    await UpdateGameAsync();
                    break;
                    //i++;
                    //continue;
                }
            }
            if (Money.GetRandomDouble(gambler) <= 0.15)
            {
                //fakeouts
                // forrest or desert
                if (i > FIELD_LENGTH / 2 - 1)
                {
                    // desert fakeout
                    await UpdateGameAsync();
                    tiles[i] = LIGHTNING;   // strike player with lightning
                    string leftTile = tiles[i - 1];
                    tiles[i - 1] = WOOSH;   // add woosh fakeout
                    await UpdateGameAsync();
                    tiles[i - 1] = leftTile; // return left tile to normal
                    tiles[i] = SHEEP;   // change back to sheep
                }
                else
                {
                    // forrest fakeout
                    await UpdateGameAsync();
                    string forrestTile = hazards[i];
                    hazards[i] = WOLF;  // add wolf
                    await UpdateGameAsync();
                    tiles[i] = FIST;    // add fist
                    await UpdateGameAsync();
                    hazards[i] = forrestTile;
                    tiles[i] = SHEEP;   // change back to sheep
                }
            }
            await UpdateGameAsync();
            tiles = MoveSheep(tiles);
            i++;
        }
        
        // payout logic
        string lambchopResultMessage;
        decimal newBalance;
        if (win)
        {
            var multi = LambchopPayoutMultiplier(targetTile);
            var lambchopPayout = Math.Round(wager * multi - wager, 2);
            newBalance = await Money.NewWagerAsync(gambler.Id, wager, lambchopPayout, WagerGame.LambChop, ct: ctx);
            lambchopResultMessage = $"{user.FormatUsername()}, you [B][COLOR={colors[BuiltIn.Keys.KiwiFarmsGreenColor].Value}]WON[/COLOR][/B]" +
                                    $" | Multi {multi} | Balance {await newBalance.FormatKasinoCurrencyAsync()}";
        }
        else
        {
            newBalance = await Money.NewWagerAsync(gambler.Id, wager, -wager, WagerGame.LambChop, ct: ctx);
            lambchopResultMessage = $"{user.FormatUsername()}, you [B][COLOR={colors[BuiltIn.Keys.KiwiFarmsRedColor].Value}]LOST[/COLOR][/B]" +
                                    $", better luck next time | Balance {await newBalance.FormatKasinoCurrencyAsync()}";
            
        }
        await botInstance.SendChatMessageAsync(lambchopResultMessage, true, autoDeleteAfter: cleanupDelay);
        return;
        
        // hacky local helper function to quickly print the current state of the game field and trigger the frame delay
        async Task UpdateGameAsync(string? updateText = null)
        {
            updateText ??= ConvertLambchopFieldToString(tiles, hazards, false);
            await botInstance.KfClient.EditMessageAsync(lambChopDisplayMessage.ChatMessageId!.Value, updateText);
            await Task.Delay(TimeSpan.FromMilliseconds(FRAME_DELAY), ctx);
        }
    }

    // return -1 if player can proceed trough entire field
    private static int CalculateDeathTile(int targetTile, GamblerDbModel gambler)
    {
        // CHECK: does player want to move all tiles?
        if (targetTile == FIELD_LENGTH)
        {
            // PLAYER WANTS TO MOVE ALL TILES
            // normal success chance
            double successChance = 1.0 / (FIELD_LENGTH + 1); // +1 because "winning" means you dont die on the last tile
            if (_houseEdge > 0)
            {
                // Decrease success chance based on houseEdge (linearly)
                successChance *= (1.0 - _houseEdge);
            }
            // Determine if player can walk all tiles
            if (Money.GetRandomDouble(gambler) <= successChance)
            {
                return -1; // No death tile (player succeeds)
            }
            else
            {
                // Player fails - calculate where the death tile appears
                double riggingFactor = Money.GetRandomDouble(gambler);
                if (_houseEdge > 0 && riggingFactor < _houseEdge * 2) // shitty hack because I made the decision to clamp houseEdge to max 50%
                {
                    // More rigging means death tile is more likely near the end
                    int minDeathTile = Math.Max(0, FIELD_LENGTH - 3);
                    return Money.GetRandomNumber(gambler, minDeathTile, FIELD_LENGTH); // return 15 means dying on the last tile xd
                }
                else
                {
                    // Player fail, random tile in the path becomes death tile
                    return Money.GetRandomNumber(gambler,0, FIELD_LENGTH);
                }
            }
        }

        // Tiles 1 - 15
        if (_houseEdge < 0.015)
        {
            int deathTile = Money.GetRandomNumber(gambler,-1, FIELD_LENGTH); // can be any tile, including no tile! (result -1 to FIELD_LENGTH (-1 - 15))
            return deathTile;
        }

        // game is rigged, manipulate tile placement
        int fairDeathTile = Money.GetRandomNumber(gambler,-1, FIELD_LENGTH);
        fairDeathTile = fairDeathTile == -1 ? FIELD_LENGTH + 1 : fairDeathTile; // shit hack, -1 means no death tile, change it to FIELD_LENGTH + 1 to compensate for next check.
        bool wouldSucceedFairly = fairDeathTile > targetTile;
        fairDeathTile = fairDeathTile == FIELD_LENGTH + 1 ? -1 : fairDeathTile;
        if (wouldSucceedFairly)
        {
            // are we gonna rig it
            double riggedFailChance = _houseEdge * 2;
            if (Money.GetRandomDouble(gambler) <= riggedFailChance)
            {
                double cruelnessLevel = Money.GetRandomDouble(gambler);
                if (cruelnessLevel < _houseEdge * 2)
                {
                    // extra rigged fail, choose tile just before target tile
                    return targetTile > 1 ? targetTile - 1 : 1;
                }
                else
                {
                    // rigging failed, normal tile return
                    return Money.GetRandomNumber(gambler,-1, targetTile);
                }

            }
            return fairDeathTile;
        }
        else
        {
            // Player would fail in fair game
            double riggingFactor = Money.GetRandomDouble(gambler);
            if (riggingFactor < _houseEdge)
            {
                // Place death tile closer to target
                // higher house edge = more likely to place closer
                int minTile = Math.Max(0, targetTile - 3);
                return Money.GetRandomNumber(gambler,minTile, targetTile);
            }
            return fairDeathTile;
        }
    }

    private static string ConvertLambchopFieldToString(List<string> tiles, List<string> hazards, bool first)
    {
        // This function takes the current state of the lambchop field and transforms it into a print ready string.
        // Its very hacky as it uses weird hairspaces to evenly space out some of the game elements for aesthetic reasons.
        // The game is optimized to display best on windows machines running a mostly default webbrowser.
        // This comes at the aesthetic expense of other platforms using different sets of emoji.
        // In case this is the first game state (bool first) print the sheep emoji in front of the tiles as to indicate
        // that the game is about to start, this prevents the game starting on a fail state on tile 0 which would look silly.
        string lambchopFieldState = "";
        int hazardSplitIndex = hazards.Count / 2; // first half of the field uses forrest emoji which need to be alternated with hairspaces for good spacing.
        string forrestHazards = string.Join(HAIRSPACE, hazards.GetRange(0, hazardSplitIndex)); // alternate forrest emoji and hairspaces
        string desertHazards = string.Concat(hazards.GetRange(hazardSplitIndex, hazards.Count - hazardSplitIndex)); // add desert emojis without spacing
        lambchopFieldState += HAZARD_SPACING + forrestHazards + desertHazards + "\n"; // glue it all together with the tiles
        lambchopFieldState += first ? SHEEP : TILE_SPACING; // first state uses sheep in front of tiles, every other state uses custom spacer string.
        lambchopFieldState += string.Join("", tiles);
        lambchopFieldState += CASTLE;
        return lambchopFieldState;
    }

    private static List<string> MoveSheep(List<string> tiles)
    {
        int index = tiles.IndexOf(SHEEP);
        if (index == -1)
        {
            // no sheep on tiles? Second game state, move sheep to first tile.
            tiles.RemoveAt(0);
            tiles.Insert(0, SHEEP);
        }
        else
        {
            if (index < tiles.Count - 1)
            {
                //tiles[index] = index < tiles.Count / 2 ? yellow_tile : purple_tile;
                tiles[index] = GREEN_TILE;
                tiles[index + 1] = SHEEP;
            }
            // sheep is already at end position
        }
        return tiles;
    }

    private static decimal LambchopPayoutMultiplier(int targetTile)
    {
        targetTile -= 1; // make it 0 indexed xd
        List<double> lambChopMultis =
        [
            1.072, 1.191, 1.331, 1.498, 1.698, 1.940, 2.238, 2.612, 3.086,
            3.704, 4.527, 5.658, 7.275, 9.700, 13.580, 20.370
        ];
        if (FIELD_LENGTH != lambChopMultis.Count)
        {
            throw new InvalidOperationException("FIELD_LENGTH doesn't match lambChopMultis array size. " +
                                                "Update the multees for the new field length");
        }
        return (decimal)lambChopMultis[targetTile];
    }

}