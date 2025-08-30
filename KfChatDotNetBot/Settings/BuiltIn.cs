using System.Text.Json;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using Microsoft.EntityFrameworkCore;
using NLog;

namespace KfChatDotNetBot.Settings;

public static class BuiltIn
{
    // Creates DB options if they don't exist and all fields (except value) if these have changed in code
    public static async Task SyncSettingsWithDb()
    {
        var logger = LogManager.GetCurrentClassLogger();
        await using var db = new ApplicationDbContext();
        logger.Info($"Syncing {BuiltInSettings.Count} settings with the DB");
        foreach (var builtIn in BuiltInSettings)
        {
            var setting = db.Settings.FirstOrDefault(setting => setting.Key == builtIn.Key);
            if (setting == null)
            {
                logger.Info($"{builtIn.Key} doesn't exist in the DB, creating");
                db.Settings.Add(new SettingDbModel
                {
                    Key = builtIn.Key,
                    Value = builtIn.Default,
                    Regex = builtIn.Regex,
                    Description = builtIn.Description,
                    Default = builtIn.Default,
                    IsSecret = builtIn.IsSecret,
                    CacheDuration = builtIn.CacheDuration.TotalSeconds,
                    ValueType = builtIn.ValueType
                });
                continue;
            }
            logger.Debug($"{builtIn.Key} exists in the DB, now going to ensure its fields are consistent");
            setting.Key = builtIn.Key;
            setting.Regex = builtIn.Regex;
            setting.Description = builtIn.Description;
            setting.Default = builtIn.Default;
            setting.IsSecret = builtIn.IsSecret;
            setting.CacheDuration = builtIn.CacheDuration.TotalSeconds;
            setting.ValueType = builtIn.ValueType;
        }
        logger.Info("Saving changes to the DB");
        await db.SaveChangesAsync();
    }

    public static async Task MigrateJsonSettingsToDb()
    {
        var oldConfigPath = "config.json";
        var logger = LogManager.GetCurrentClassLogger();
        await using var db = new ApplicationDbContext();
        
        logger.Info($"Checking {oldConfigPath} exists");
        if (!Path.Exists(oldConfigPath))
        {
            logger.Info($"{oldConfigPath} does not exist. Migration already performed or was never needed");
            return;
        }
        
        logger.Info($"Migrating {oldConfigPath}");
#pragma warning disable CS0612 // Type or member is obsolete
        var oldConfig = JsonSerializer.Deserialize<ConfigModel>(await File.ReadAllTextAsync(oldConfigPath));
#pragma warning restore CS0612 // Type or member is obsolete
        if (oldConfig == null)
        {
            logger.Error($"Caught a null when deserializing {oldConfigPath}");
            return;
        }

        await SettingsProvider.SetValueAsync(Keys.PusherEndpoint, oldConfig.PusherEndpoint.ToString());
        await SettingsProvider.SetValueAsync(Keys.KiwiFarmsWsEndpoint, oldConfig.KfWsEndpoint.ToString());
        await SettingsProvider.SetValueAsync(Keys.KiwiFarmsRoomId, oldConfig.KfChatRoomId);
        await SettingsProvider.SetValueAsync(Keys.Proxy, oldConfig.Proxy);
        await SettingsProvider.SetValueAsync(Keys.KiwiFarmsWsReconnectTimeout, oldConfig.KfReconnectTimeout);
        await SettingsProvider.SetValueAsync(Keys.PusherReconnectTimeout, oldConfig.PusherReconnectTimeout);
        await SettingsProvider.SetValueAsBooleanAsync(Keys.GambaSeshDetectEnabled, oldConfig.EnableGambaSeshDetect);
        await SettingsProvider.SetValueAsync(Keys.GambaSeshUserId, oldConfig.GambaSeshUserId);
        await SettingsProvider.SetValueAsync(Keys.KickIcon, oldConfig.KickIcon);
        await SettingsProvider.SetValueAsync(Keys.KiwiFarmsDomain, oldConfig.KfDomain);
        await SettingsProvider.SetValueAsync(Keys.KiwiFarmsUsername, oldConfig.KfUsername);
        await SettingsProvider.SetValueAsync(Keys.KiwiFarmsPassword, oldConfig.KfPassword);
        await SettingsProvider.SetValueAsync(Keys.TwitchBossmanJackId, oldConfig.BossmanJackTwitchId);
        await SettingsProvider.SetValueAsync(Keys.TwitchBossmanJackUsername, oldConfig.BossmanJackTwitchUsername);
        await SettingsProvider.SetValueAsBooleanAsync(Keys.KiwiFarmsSuppressChatMessages, oldConfig.SuppressChatMessages);
        await SettingsProvider.SetValueAsync(Keys.DiscordToken, oldConfig.DiscordToken);
        await SettingsProvider.SetValueAsync(Keys.DiscordBmjId, oldConfig.DiscordBmjId);
        logger.Info($"{oldConfigPath} migration done.");

        logger.Info("Renaming files no longer in use");
        // Utils.SafelyRenameFile will attempt to rename and swallow any exception (with logging) if it fails
        SafelyRenameFile(oldConfigPath, $"{oldConfigPath}.migrated");

        logger.Info("File renamed");
    }
#pragma warning disable CS0612 // Type or member is obsolete
    public static async Task MigrateStreamChannelsToDatabase()
    {
        var logger = LogManager.GetCurrentClassLogger();
        await using var db = new ApplicationDbContext();
        if (await db.Streams.AnyAsync())
        {
            logger.Info("Streams already migrated as there are rows in the table");
            return;
        }
        var channels =
            await SettingsProvider.GetMultipleValuesAsync([Keys.KickChannels, Keys.PartiChannels]);
        var kickChannels = channels[Keys.KickChannels].JsonDeserialize<List<KickChannelModel>>();
        foreach (var channel in kickChannels ?? [])
        {
            var user = await db.Users.FirstAsync(u => u.KfId == channel.ForumId);
            await db.Streams.AddAsync(new StreamDbModel
            {
                StreamUrl = $"https://kick.com/{channel.ChannelSlug}",
                User = user,
                AutoCapture = channel.AutoCapture,
                Metadata = JsonSerializer.Serialize(new KickStreamMetaModel { ChannelId = channel.ChannelId } ),
                Service = StreamService.Kick
            });
            logger.Info($"Migrated {channel.ChannelSlug} Kick channel");
        }

        var partiChannels = channels[Keys.PartiChannels].JsonDeserialize<List<PartiChannelModel>>();
#pragma warning restore CS0612 // Type or member is obsolete
        foreach (var channel in partiChannels ?? [])
        {
            var user = await db.Users.FirstAsync(u => u.KfId == channel.ForumId);
            var streamUrl = $"https://parti.com/creator/{channel.SocialMedia}/{channel.Username}/";
            if (channel.SocialMedia == "discord")
            {
                streamUrl += "0";
            }
            await db.Streams.AddAsync(new StreamDbModel
            {
                StreamUrl = streamUrl,
                AutoCapture = channel.AutoCapture,
                Service = StreamService.Parti,
                User = user
            });
            logger.Info($"Migrated {channel.Username} Parti channel");
        }

        await db.SaveChangesAsync();
    }
    
    private static void SafelyRenameFile(string oldName, string newName)
    {
        var logger = LogManager.GetCurrentClassLogger();
        logger.Debug($"Renaming {oldName} to {newName}");
        try
        {
            File.Move(oldName, newName);
        }
        catch (Exception e)
        {
            logger.Error($"Failed to rename {oldName} to {newName}");
            logger.Error(e);
        }
    }

    private const string BooleanRegex = "true|false";
    private const string WholeNumberRegex = @"\d+";
    
    public static List<BuiltInSettingsModel> BuiltInSettings =
    [
        new BuiltInSettingsModel
        {
            Key = Keys.PusherEndpoint,
            Description =
                "Pusher WebSocket endpoint URL",
            Default = "wss://ws-us2.pusher.com/app/32cbd69e4b950bf97679?protocol=7&client=js&version=7.6.0&flash=false",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KiwiFarmsWsEndpoint,
            Description =
                "Kiwi Farms chat WebSocket endpoint",
            Default = "wss://kiwifarms.st:9443/chat.ws",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KiwiFarmsRoomId,
            Regex = WholeNumberRegex,
            Description =
                "Kiwi Farms Keno Kasino room ID",
            Default = "15",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.Proxy,
            Description =
                "Proxy to use for all outgoing requests. Null to disable",
            Default = null,
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KiwiFarmsWsReconnectTimeout,
            Regex = WholeNumberRegex,
            Description =
                "Kiwi Farms chat reconnect timeout",
            Default = "30",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.PusherReconnectTimeout,
            Regex = WholeNumberRegex,
            Description =
                "Pusher reconnect timeout",
            Default = "30",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.GambaSeshDetectEnabled,
            Regex = BooleanRegex,
            Description =
                "Whether to enable detection for the presence of GambaSesh",
            Default = "true",
            CacheDuration = TimeSpan.FromMinutes(5),
            ValueType = SettingValueType.Boolean
        },
        new BuiltInSettingsModel
        {
            Key = Keys.GambaSeshUserId,
            Regex = WholeNumberRegex,
            Description =
                "GambaSesh's uer ID for the purposes of detection",
            Default = "168162",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KickIcon,
            Description =
                "Kick Icon to use for relaying chat messages",
            Default = "https://i.postimg.cc/Qtw4nCPG/kick16.png",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KiwiFarmsDomain,
            Description =
                "Domain to use when retrieving a session token",
            Default = "kiwifarms.st",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KiwiFarmsUsername,
            Description =
                "Username to use when authenticating with Kiwi Farms",
            Default = null,
            IsSecret = true,
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KiwiFarmsPassword,
            Description =
                "Password to use when authenticating with Kiwi Farms",
            Default = null,
            IsSecret = true,
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.TwitchBossmanJackId,
            Regex = WholeNumberRegex,
            Description =
                "BossmanJack's Twitch channel ID",
            Default = "114122847",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.TwitchBossmanJackUsername,
            Description =
                "BossmanJack's Twitch channel username",
            Default = "thebossmanjack",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KiwiFarmsSuppressChatMessages,
            Regex = BooleanRegex,
            Description =
                "Enable to prevent messages from actually being sent to chat.",
            Default = "false",
            ValueType = SettingValueType.Boolean
        },
        new BuiltInSettingsModel
        {
            Key = Keys.DiscordToken,
            Description =
                "Token to use when authenticating with Discord. Set to null to disable.",
            Default = null,
            IsSecret = true,
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.DiscordBmjId,
            Regex = WholeNumberRegex,
            Description =
                "BossmanJack's Discord user ID",
            Default = "554123642246529046",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.TwitchIcon,
            Description = "URL for the 16px Twitch icon",
            Default = "https://i.postimg.cc/QMFVV2Xk/twitch16.png",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.DiscordIcon,
            Description = "URL for the 16px Discord icon",
            Default = "https://i.postimg.cc/cLmQrp89/discord16.png",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.ShuffleBmjUsername,
            Description = "Bossman's Shuffle Username",
            Default = "TheBossmanJack",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.JuiceCooldown,
            Regex = WholeNumberRegex,
            Description = "Cooldown (in seconds) until you can get juiced again",
            Default = "3600",
            CacheDuration = TimeSpan.FromMinutes(5),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.JuiceAmount,
            Regex = WholeNumberRegex,
            Description = "Amount of $KKK to juice",
            Default = "50",
            CacheDuration = TimeSpan.FromMinutes(5),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KickEnabled,
            Regex = BooleanRegex,
            Description = "Whether to enable Kick functionality (Pusher websocket mainly)",
            Default = "true",
            ValueType = SettingValueType.Boolean
        },
        new BuiltInSettingsModel
        {
            Key = Keys.HowlggDivisionAmount,
            Regex = WholeNumberRegex,
            Description = "How much to divide the Howlgg bets/profit by to get the real value",
            Default = "1650",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel()
        {
            Key = Keys.KiwiFarmsGreenColor,
            Description = "Green color used for showing positive values in chat",
            Default = "#3dd179",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel()
        {
            Key = Keys.KiwiFarmsRedColor,
            Description = "Red color used for showing negative values in chat",
            Default = "#f1323e",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel()
        {
            Key = Keys.JackpotBmjUsernames,
            Description = "Bossman's usernames on Jackpot",
            Default = "[\"TheBossmanJack\", \"Austingambless757\"]",
            ValueType = SettingValueType.Array
        },
        new BuiltInSettingsModel
        {
            Key = Keys.RainbetBmjPublicIds,
            Description = "Bossman's rainbet public IDs",
            Default = "[\"Ir04170wLulcjtePCL7P6lmeOlepRaNp\", \"IA9RHFR1NLHL33AVOM9GL2G2CINM9I6P\"]",
            ValueType = SettingValueType.Array
        },
        new BuiltInSettingsModel
        {
            Key = Keys.FlareSolverrApiUrl,
            Description = "URL for your FlareSolverr service API",
            Default = "http://localhost:8191/",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.FlareSolverrProxy,
            Description = "Proxy in use specifically for FlareSolverr",
            Default = null,
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.ChipsggBmjUserIds,
            Description = "Bossman's Chips.gg username",
            //Default = "[\"TheBossmanJack\", \"Yabuddy757\"]",
            Default = "[\"1af247cd-67e0-4029-8a93-b8d19c275072\", \"e97ebf3e-d5a8-4583-ab35-5095a05f282e\"]",
            ValueType = SettingValueType.Array
        },
        new BuiltInSettingsModel
        {
            Key = Keys.RestreamUrl,
            Description = "URL for the restream",
            Default = "No URL set",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.HowlggBmjUserId,
            Regex = WholeNumberRegex,
            Description = "BMJ's user ID on howl.gg",
            Default = "951905",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KiwiFarmsCookies,
            Description = "Kiwi Farms cookies in key-value pair format",
            // Empty JSON object as it's a Dictionary<string, string> object
            Default = "{}",
            IsSecret = true,
            ValueType = SettingValueType.Complex
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KiwiFarmsInactivityTimeout,
            Regex = WholeNumberRegex,
            // You would think the WS library would trip up with the "NoMessageReceived" exception, but there's some bug
            // where it'll occasionally fail to reconnect properly and sit there dead forever, hence the watchdog timer
            Description = "Length of time the client can go without receiving ANY packets from Sneedchat before forcing a reconnect.",
            Default = "300",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KiwiFarmsPingInterval,
            Regex = WholeNumberRegex,
            Description = "Interval in seconds to ping Sneedchat using the non-existent /ping command. " +
                          "Note this affects how often the bot will check inactivity of the connection.",
            Default = "10",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.JuiceLoserDivision,
            Regex = WholeNumberRegex,
            Description = "Amount to divide the juice by if the user's rack is Loser",
            Default = "5",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.CrackedZalgoFuckUpMode,
            Regex = WholeNumberRegex,
            Description = "FuckUpMode. 0 = Min, 1 = Normal, 2 = Max",
            Default = "1",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.CrackedZalgoFuckUpPosition,
            Regex = WholeNumberRegex,
            Description = "FuckUpPosition: 1 = Up, 2 = Middle, 3 = UpAndMiddle, 4 = Bot (Bottom), 5 = UpAndBot, 6 = MiddleAndBot, 7 = All",
            Default = "2",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BotDisconnectReplayLimit,
            Regex = WholeNumberRegex,
            Description = "Limit of messages which could not be sent while bot was disconnected to replay on connect",
            Default = "10",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KiwiFarmsJoinFailLimit,
            Regex = WholeNumberRegex,
            Description = "Limit of times to fail joining the room before wiping cookies",
            Default = "2",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
#pragma warning disable CS0612 // Type or member is obsolete
            Key = Keys.KickChannels,
#pragma warning restore CS0612 // Type or member is obsolete
            Description = "Kick channels the bot knows about for notifications",
            Default = "[]",
            ValueType = SettingValueType.Array
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BotCleanStartTime,
            Description = "ISO8601 date of Bossman's sobriety",
            Default = "2024-09-19T13:33:00-04:00",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BotRehabEndTime,
            Description = "ISO8601 date of Bossman's rehab end",
            Default = "2024-10-24T09:00:00-04:00",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BotPoNextVisit,
            Description = "ISO8601 date of Bossman's next PO visit",
            Default = "2024-10-18T12:00:00-04:00",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BotJailStartTime,
            Description = "ISO8601 date of when Bossman's incarceration began",
            Default = "2024-10-27T03:25:00-05:00",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BotCourtCalendar,
            Description = "JSON array containing court hearings",
            Default = "[]",
            CacheDuration = TimeSpan.Zero,
            ValueType = SettingValueType.Complex
        },
        new BuiltInSettingsModel
        {
            Key = Keys.HowlggEnabled,
            Regex = BooleanRegex,
            Description = "Whether the Howl.gg integration is enabled at all",
            Default = "false",
            ValueType = SettingValueType.Boolean
        },
        new BuiltInSettingsModel
        {
            Key = Keys.ChipsggEnabled,
            Regex = BooleanRegex,
            Description = "Whether the Chips.gg integration is enabled at all",
            Default = "false",
            ValueType = SettingValueType.Boolean
        },
        new BuiltInSettingsModel
        {
            Key = Keys.RainbetEnabled,
            Regex = BooleanRegex,
            Description = "Whether the Rainbet integration is enabled at all",
            Default = "false",
            ValueType = SettingValueType.Boolean
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BotImageAcceptableKeys,
            Description = "List of valid keys for the image rotation feature",
            Default = "[\"gmkasino\", \"gnkasino\", \"winmanjack\", \"prayge\", \"crackpipe\", \"bassmanjack\", \"sent\", \"helpme\"]",
            ValueType = SettingValueType.Array
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BotToyStoryImage,
            Description = "Image to use for the Toy Story joke",
            Default = "https://i.ibb.co/603dk32R/nonce-drop.png",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BotImageRandomSliceDivideBy,
            Regex = WholeNumberRegex,
            Description = "What value to divide the image count by for determining how many images to randomly choose from. " +
                          "e.g. a value of 10 on 50 images means the 5 least seen images are chosen from randomly. " +
                          "If the count of images is =< this value, it'll just grab the oldest image. " +
                          "Fractions will be rounded, so a value of 5 with 7 images will round down and take the oldest image.",
            Default = "5",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BotChrisDjLiveImage,
            Description = "Image that the bot will send when ChrisDJ goes live",
            Default = "https://kiwifarms.st/attachments/nonce-live-png.7015533/",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.DiscordTemporarilyBypassGambaSeshInitialValue,
            Regex = BooleanRegex,
            Description = "What the initial value of the Discord GambaSesh temporary bypass variable should be",
            Default = "false",
            ValueType = SettingValueType.Boolean
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BotKeesSeen,
            Regex = BooleanRegex,
            Description = "Track if Kees has been seen so users can receive a one-time notice if he suddenly shows up",
            Default = "false",
            ValueType = SettingValueType.Boolean
        },
        new BuiltInSettingsModel
        {
            Key = Keys.ClashggEnabled,
            Regex = BooleanRegex,
            Description = "Whether the Clash.gg integration should be enabled",
            Default = "true",
            ValueType = SettingValueType.Boolean
        },
        new BuiltInSettingsModel
        {
            Key = Keys.ClashggBmjIds,
            Description = "List of IDs that austingambles is using",
            Default = "[]",
            ValueType = SettingValueType.Array
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BotAlmanacText,
            Description = "Text to send when reminding people of the Almanac",
            Default = "Placeholder text for the Almanac",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BotAlmanacInterval,
            Regex = WholeNumberRegex,
            Description = "Interval for Almanac reminders in seconds",
            Default = "14400", // 4 hours
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BotAlmanacInitialState,
            Regex = BooleanRegex,
            Description = "Initial state of the Almanac reminder",
            Default = "false",
            ValueType = SettingValueType.Boolean
        },
        new BuiltInSettingsModel
        {
            Key = Keys.JuiceAllowedWhileStreaming,
            Regex = BooleanRegex,
            Description = "Whether to allow juicers while Austin is streaming",
            Default = "false",
            ValueType = SettingValueType.Boolean
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BotImagePigCubeSelfDestruct,
            Regex = BooleanRegex,
            Description = "Whether the pigcube should self destruct after a random interval",
            Default = "true",
            ValueType = SettingValueType.Boolean
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BotImageInvertedCubeUrl,
            Description = "URL of the inverted pig cube for the special deletion logic",
            Default = "https://kiwifarms.st/attachments/7226614-185d31e0b73350f2765b8051121a05d2-webp.7271720/",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.JuiceAutoDeleteMsgDelay,
            Regex = WholeNumberRegex,
            Description = "Delay before deleting the !juice message in milliseconds, null or 0 to disable. " +
                          "Don't set too high as the timeout for !juiceme is 60 seconds",
            Default = "2500",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BotImagePigCubeSelfDestructMin,
            Regex = WholeNumberRegex,
            Description = "Min value for the Pig Cube self destruct Random.Next() in milliseconds",
            Default = "5000",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BotImagePigCubeSelfDestructMax,
            Regex = WholeNumberRegex,
            Description = "Max value for the Pig Cube self destruct Random.Next() in milliseconds",
            Default = "15000",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BotImageInvertedPigCubeSelfDestructDelay,
            Regex = WholeNumberRegex,
            Description = "Value in milliseconds for how long the bot should wait before self destructing the inverted pig cube",
            Default = "5000",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BetBoltEnabled,
            Regex = BooleanRegex,
            Description = "Whether to enable the BetBolt bet feed tracking",
            Default = "true",
            ValueType = SettingValueType.Boolean
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BetBoltBmjUsernames,
            Description = "Austin's usernames on BetBolt",
            Default = "[\"AustinGambles\"]",
            ValueType = SettingValueType.Array
        },
        new BuiltInSettingsModel
        {
            Key = Keys.YeetEnabled,
            Regex = BooleanRegex,
            Description = "Whether to enable the Yeet bet feed tracking",
            Default = "true",
            ValueType = SettingValueType.Boolean
        },
        new BuiltInSettingsModel
        {
            Key = Keys.YeetBmjUsernames,
            Description = "Austin's usernames on Yeet",
            Default = "[\"Bossmanjack\"]",
            ValueType = SettingValueType.Array
        },
        new BuiltInSettingsModel
        {
            Key = Keys.YeetProxy,
            Description = "Proxy to use for Yeet",
            Default = "socks5://ca-van-wg-socks5-301.relays.mullvad.net:1080",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.MomCooldown,
            Regex = WholeNumberRegex,
            Description = "Cooldown in seconds for the mom command, 0 to disable",
            Default = "30",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.CaptureYtDlpOutputFormat,
            Description = "Output format to pass to yt-dlp using -o",
            Default = "%(title)s - %(uploader)s [%(id)s] %(upload_date)s %(timestamp)s.mp4",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.CaptureYtDlpWorkingDirectory,
            Description = "Working directory set when running yt-dlp",
            Default = "/tmp/discord/",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.CaptureYtDlpBinaryPath,
            Description = "Path of the yt-dlp binary",
            Default = "/usr/local/bin/yt-dlp",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.CaptureYtDlpUserAgent,
            Description = "User-Agent that gets passed to yt-dlp --user-agent",
            Default = "Mozilla/5.0 (X11; Linux x86_64; rv:128.0) Gecko/20100101 Firefox/128.0",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.CaptureYtDlpCookiesFromBrowser,
            Description = "What browser to pass to yt-dlp --cookies-from-browser",
            Default = "firefox",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.CaptureEnabled,
            Description = "Whether the auto-capture system is enabled",
            Regex = BooleanRegex,
            Default = "true",
            ValueType = SettingValueType.Boolean
        },
        new BuiltInSettingsModel
        {
            Key = Keys.CaptureYtDlpParentTerminal,
            Description = "Parent terminal to launch the capture inside of, e.g. xfce4-terminal, mate-terminal, etc. " +
                          "Terminal must support -x. The process detaches from the bot so the bot can be freely restarted while a capture is running. " +
                          "Not supported on Windows, the bot will use cmd /C + START on Windows.",
            Default = "/usr/bin/mate-terminal",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.CaptureYtDlpScriptPath,
            Description = "Path to store the temporary .sh script used to initiate the capture",
            Default = "/tmp/",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.PartiEnabled,
            Regex = BooleanRegex,
            Description = "Whether the Parti stream notification service is enabled",
            Default = "true",
            ValueType = SettingValueType.Boolean
        },
        new BuiltInSettingsModel
        {
#pragma warning disable CS0612 // Type or member is obsolete
            Key = Keys.PartiChannels,
#pragma warning restore CS0612 // Type or member is obsolete
            Description = "JSON of all the Parti channels to listen to",
            Default = "[]",
            ValueType = SettingValueType.Complex
        },       
        new BuiltInSettingsModel
        {
            Key = Keys.CaptureStreamlinkOutputFormat,
            Description = "Output format to pass to streamlink using --output",
            Default = "{author}-{id}-{title}-{time:%Y-%m-%d_%Hh%Mm%Ss}.ts",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.CaptureStreamlinkBinaryPath,
            Description = "Path of the streamlink binary",
            Default = "/usr/local/bin/streamlink",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.CaptureStreamlinkRemuxScript,
            Description = "Path of the remux script to convert .ts to .mp4",
            Default = "pwsh /root/BMJ/Convert-TsToMp4.ps1",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.DLiveCheckInterval,
            Description = "How often (in seconds) to check if a DLive streamer is live",
            Default = "15",
            Regex = WholeNumberRegex,
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.DLivePersistedCurrentlyLiveStreams,
            Description = "Array of DLive streamers who are currently live for persistence between bot restarts",
            Default = "[]",
            ValueType = SettingValueType.Complex
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KiwiPeerTubePersistedCurrentlyLiveStreams,
            Description = "Array of Kiwi PeerTube stream GUIDs which are currently live for persistence between bot restarts",
            Default = "[]",
            ValueType = SettingValueType.Complex
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KiwiPeerTubeEnabled,
            Description = "Whether the Kiwi PeerTube live notification is enabled",
            Default = "true",
            ValueType = SettingValueType.Boolean,
            Regex = BooleanRegex
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KiwiPeerTubeCheckInterval,
            Description = "Interval (in seconds) to check live streams",
            Default = "10",
            ValueType = SettingValueType.Text,
            Regex = WholeNumberRegex
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KiwiPeerTubeEnforceWhitelist,
            Description = "Whether to enforce the use of a whitelist (i.e. streamer must be in the database)",
            Default = "false",
            ValueType = SettingValueType.Boolean,
            Regex = BooleanRegex
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BotDeadBotDetectionInterval,
            Description = "Interval in seconds for checking if the bot is completely dead",
            Default = "15",
            ValueType = SettingValueType.Text,
            Regex = WholeNumberRegex
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BotExitOnDeath,
            Description = "Whether the bot should exit if it's dead and unrecoverable",
            Default = "true",
            ValueType = SettingValueType.Boolean,
            Regex = BooleanRegex
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BotRespondToDiscordImpersonation,
            Description = "Whether to respond to Bossman impersonations",
            Default = "true",
            ValueType = SettingValueType.Boolean,
            Regex = BooleanRegex
        },
        new BuiltInSettingsModel
        {
            Key = Keys.MoneySymbolSuffix,
            Description = "What is the symbol of the bot's currency when used as a suffix for an amount",
            Default = "KKK",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.MoneySymbolPrefix,
            Description = "What is the symbol of the bot's currency when used as a prefix for an amount",
            Default = "KKK$",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.MoneyEnabled,
            Description = "Whether the monetary system is enabled at all. " +
                          "If disabled, the bot won't answer any commands related to balance, transactions, gambling, etc.",
            Default = "false",
            ValueType = SettingValueType.Boolean,
            Regex = BooleanRegex
        },
        new BuiltInSettingsModel
        {
            Key = Keys.MoneyInitialBalance,
            Description = "Gambler's initial balance on creation",
            Default = "100",
            ValueType = SettingValueType.Text,
            Regex = WholeNumberRegex
        },
        new BuiltInSettingsModel
        {
            Key = Keys.TwitchGraphQlCheckInterval,
            Description = "Interval in seconds to check if Bossman is live on Twitch using GraphQL",
            Default = "10",
            ValueType = SettingValueType.Text,
            Regex = WholeNumberRegex
        },
        new BuiltInSettingsModel
        {
            Key = Keys.TwitchGraphQlPersistedCurrentlyLive,
            Description = "Whether BossmanJack is currently live on Twitch",
            Default = "false",
            ValueType = SettingValueType.Boolean,
            Regex = BooleanRegex
        },
        new BuiltInSettingsModel
        {
            Key = Keys.CaptureStreamlinkTwitchOptions,
            Description = "Special options for Twitch streams captured with Streamlink",
            Default = "--twitch-disable-ad --twitch-proxy-playlist=https://eu.luminous.dev,https://eu2.luminous.dev,https://as.luminous.dev,https://cdn.perfprod.com",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.OwncastCheckInterval,
            Description = "Interval in seconds to check if someone is live on Owncast",
            Default = "5",
            ValueType = SettingValueType.Text,
            Regex = WholeNumberRegex
        },
        new BuiltInSettingsModel
        {
            Key = Keys.OwncastPersistedCurrentlyLive,
            Description = "Whether someone is live on Owncast",
            Default = "false",
            ValueType = SettingValueType.Boolean,
            Regex = BooleanRegex
        },
        new BuiltInSettingsModel
        {
            Key = Keys.MoneyRakebackPercentage,
            Description = "Percentage of total wagered amount that should be returned for rakeback.",
            Default = "2.5",
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.MoneyRakebackMinimumAmount,
            Description = "Minimum rakeback to pay out. Anything below this will tell the user to go away.",
            Default = "10",
            ValueType = SettingValueType.Text,
            Regex = WholeNumberRegex
        },
    ];
    
    public static class Keys
    {
        public static string PusherEndpoint = "Pusher.Endpoint";
        public static string KiwiFarmsWsEndpoint = "KiwiFarms.WsEndpoint";
        public static string KiwiFarmsRoomId = "KiwiFarms.RoomId";
        public static string Proxy = "Proxy";
        public static string KiwiFarmsWsReconnectTimeout = "KiwiFarms.WsReconnectTimeout";
        public static string PusherReconnectTimeout = "Pusher.ReconnectTimeout";
        public static string GambaSeshDetectEnabled = "GambaSesh.DetectEnabled";
        public static string GambaSeshUserId = "GambaSesh.UserId";
        public static string KickIcon = "Kick.Icon";
        public static string KiwiFarmsDomain = "KiwiFarms.Domain";
        public static string KiwiFarmsUsername = "KiwiFarms.Username";
        public static string KiwiFarmsPassword = "KiwiFarms.Password";
        public static string TwitchBossmanJackId = "Twitch.BossmanJackId";
        public static string TwitchBossmanJackUsername = "Twitch.BossmanJackUsername";
        public static string KiwiFarmsSuppressChatMessages = "KiwiFarms.SuppressChatMessages";
        public static string DiscordToken = "Discord.Token";
        public static string DiscordBmjId = "Discord.BmjId";
        public static string TwitchIcon = "Twitch.Icon";
        public static string DiscordIcon = "Discord.Icon";
        public static string ShuffleBmjUsername = "Shuffle.BmjUsername";
        public static string JuiceCooldown = "Juice.Cooldown";
        public static string JuiceAmount = "Juice.Amount";
        public static string KickEnabled = "Kick.Enabled";
        public static string HowlggDivisionAmount = "Howlgg.DivisionAmount";
        public static string HowlggBmjUserId = "Howlgg.BmjUserId";
        public static string KiwiFarmsGreenColor = "KiwiFarms.GreenColor";
        public static string KiwiFarmsRedColor = "KiwiFarms.RedColor";
        public static string JackpotBmjUsernames = "Jackpot.BmjUsernames";
        public static string RainbetBmjPublicIds = "Rainbet.BmjPublicIds";
        public static string FlareSolverrApiUrl = "FlareSolverr.ApiUrl";
        public static string FlareSolverrProxy = "FlareSolverr.Proxy";
        public static string ChipsggBmjUserIds = "Chipsgg.BmjUserIds";
        public static string RestreamUrl = "RestreamUrl";
        public static string KiwiFarmsCookies = "KiwiFarms.Cookies";
        public static string KiwiFarmsInactivityTimeout = "KiwiFarms.InactivityTimeout";
        public static string KiwiFarmsPingInterval = "KiwiFarms.PingInterval";
        public static string JuiceLoserDivision = "Juice.LoserDivision";
        public static string CrackedZalgoFuckUpMode = "Cracked.ZalgoFuckUpMode";
        public static string CrackedZalgoFuckUpPosition = "Cracked.ZalgoFuckUpPosition";
        public static string BotDisconnectReplayLimit = "Bot.DisconnectReplayLimit";
        public static string KiwiFarmsJoinFailLimit = "KiwiFarms.JoinFailLimit";
        [Obsolete]
        public static string KickChannels = "Kick.Channels";
        public static string BotCleanStartTime = "Bot.Clean.StartTime";
        public static string BotRehabEndTime = "Bot.Rehab.EndTime";
        public static string BotPoNextVisit = "Bot.Po.NextVisit";
        public static string BotJailStartTime = "Bot.Jail.StartTime";
        public static string BotCourtCalendar = "Bot.Court.Calendar";
        public static string HowlggEnabled = "Howlgg.Enabled";
        public static string ChipsggEnabled = "Chipsgg.Enabled";
        public static string RainbetEnabled = "Rainbet.Enabled";
        public static string BotImageAcceptableKeys = "Bot.Image.AcceptableKeys";
        public static string BotToyStoryImage = "Bot.ToyStoryImage";
        public static string BotImageRandomSliceDivideBy = "Bot.Image.RandomSliceDivideBy";
        public static string BotChrisDjLiveImage = "Bot.ChrisDjLiveImage";
        public static string DiscordTemporarilyBypassGambaSeshInitialValue =
            "Discord.TemporarilyBypassGambaSeshInitialValue";
        public static string BotKeesSeen = "Bot.KeesSeen";
        public static string ClashggEnabled = "Clashgg.Enabled";
        public static string ClashggBmjIds = "Clashgg.BmjIds";
        public static string BotAlmanacText = "Bot.Almanac.Text";
        public static string BotAlmanacInterval = "Bot.Almanac.Interval";
        public static string BotAlmanacInitialState = "Bot.Almanac.InitialState";
        public static string JuiceAllowedWhileStreaming = "Juice.AllowedWhileStreaming";
        public static string BotImagePigCubeSelfDestruct = "Bot.Image.PigCubeSelfDestruct";
        public static string BotImageInvertedCubeUrl = "Bot.Image.InvertedCubeUrl";
        public static string JuiceAutoDeleteMsgDelay = "Juice.AutoDeleteMsgDelay";
        public static string BotImagePigCubeSelfDestructMin = "Bot.Image.PigCubeSelfDestructMin";
        public static string BotImagePigCubeSelfDestructMax = "Bot.Image.PigCubeSelfDestructMax";
        public static string BotImageInvertedPigCubeSelfDestructDelay = "Bot.Image.InvertedPigCubeSelfDestructDelay";
        public static string BetBoltEnabled = "BetBolt.Enabled";
        public static string BetBoltBmjUsernames = "BetBolt.BmjUsernames";
        public static string YeetEnabled = "Yeet.Enabled";
        public static string YeetBmjUsernames = "Yeet.BmjUsernames";
        public static string YeetProxy = "Yeet.Proxy";
        public static string MomCooldown = "Mom.Cooldown";
        public static string CaptureYtDlpOutputFormat = "Capture.YtDlp.OutputFormat";
        public static string CaptureYtDlpWorkingDirectory = "Capture.YtDlp.WorkingDirectory";
        public static string CaptureYtDlpBinaryPath = "Capture.YtDlp.BinaryPath";
        public static string CaptureYtDlpUserAgent = "Capture.YtDlp.UserAgent";
        public static string CaptureYtDlpCookiesFromBrowser = "Capture.YtDlp.CookiesFromBrowser";
        public static string CaptureEnabled = "Capture.Enabled";
        public static string CaptureYtDlpParentTerminal = "Capture.YtDlp.ParentTerminal";
        public static string CaptureYtDlpScriptPath = "Capture.YtDlp.ScriptPath";
        public static string PartiEnabled = "Parti.Enabled";
        [Obsolete]
        public static string PartiChannels = "Parti.Channels";
        public static string CaptureStreamlinkBinaryPath = "Capture.Streamlink.BinaryPath";
        public static string CaptureStreamlinkOutputFormat = "Capture.Streamlink.OutputFormat";
        public static string CaptureStreamlinkRemuxScript = "Capture.Streamlink.RemuxScript";
        public static string CaptureStreamlinkTwitchOptions = "Capture.Streamlink.TwitchOptions";
        public static string DLiveCheckInterval = "DLive.CheckInterval";
        public static string DLivePersistedCurrentlyLiveStreams = "DLive.PersistedCurrentlyLiveStreams";
        public static string KiwiPeerTubePersistedCurrentlyLiveStreams = "KiwiPeerTube.PersistedCurrentlyLiveStreams";
        public static string KiwiPeerTubeEnabled = "KiwiPeerTube.Enabled";
        public static string KiwiPeerTubeCheckInterval = "KiwiPeerTube.CheckInterval";
        public static string KiwiPeerTubeEnforceWhitelist = "KiwiPeerTube.EnforceWhitelist";
        public static string BotDeadBotDetectionInterval = "Bot.DeadBotDetectionInterval";
        public static string BotExitOnDeath = "Bot.ExitOnDeath";
        public static string BotRespondToDiscordImpersonation = "Bot.RespondToDiscordImpersonation";
        public static string MoneySymbolSuffix = "Money.SymbolSuffix";
        public static string MoneySymbolPrefix = "Money.SymbolPrefix";
        public static string MoneyEnabled = "Money.Enabled";
        public static string MoneyInitialBalance = "Money.InitialBalance";
        public static string TwitchGraphQlCheckInterval = "TwitchGraphQl.CheckInterval";
        public static string TwitchGraphQlPersistedCurrentlyLive = "TwitchGraphQl.PersistedCurrentlyLive";
        public static string OwncastCheckInterval = "Owncast.CheckInterval";
        public static string OwncastPersistedCurrentlyLive = "Owncast.PersistedCurrentlyLive";
        public static string MoneyRakebackPercentage = "Money.Rakeback.Percentage";
        public static string MoneyRakebackMinimumAmount = "Money.Rakeback.MinimumAmount";
    }
}