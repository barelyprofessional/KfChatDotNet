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

        await Helpers.SetValue(Keys.PusherEndpoint, oldConfig.PusherEndpoint.ToString());
        await Helpers.SetValue(Keys.KiwiFarmsWsEndpoint, oldConfig.KfWsEndpoint.ToString());
        await Helpers.SetValueAsList(Keys.PusherChannels, oldConfig.PusherChannels);
        await Helpers.SetValue(Keys.KiwiFarmsRoomId, oldConfig.KfChatRoomId);
        await Helpers.SetValue(Keys.Proxy, oldConfig.Proxy);
        await Helpers.SetValue(Keys.KiwiFarmsWsReconnectTimeout, oldConfig.KfReconnectTimeout);
        await Helpers.SetValue(Keys.PusherReconnectTimeout, oldConfig.PusherReconnectTimeout);
        await Helpers.SetValueAsBoolean(Keys.GambaSeshDetectEnabled, oldConfig.EnableGambaSeshDetect);
        await Helpers.SetValue(Keys.GambaSeshUserId, oldConfig.GambaSeshUserId);
        await Helpers.SetValue(Keys.KickIcon, oldConfig.KickIcon);
        await Helpers.SetValue(Keys.KiwiFarmsDomain, oldConfig.KfDomain);
        await Helpers.SetValue(Keys.KiwiFarmsUsername, oldConfig.KfUsername);
        await Helpers.SetValue(Keys.KiwiFarmsPassword, oldConfig.KfPassword);
        await Helpers.SetValue(Keys.KiwiFarmsChromiumPath, oldConfig.ChromiumPath);
        await Helpers.SetValue(Keys.TwitchBossmanJackId, oldConfig.BossmanJackTwitchId);
        await Helpers.SetValue(Keys.TwitchBossmanJackUsername, oldConfig.BossmanJackTwitchUsername);
        await Helpers.SetValueAsBoolean(Keys.KiwiFarmsSuppressChatMessages, oldConfig.SuppressChatMessages);
        await Helpers.SetValue(Keys.DiscordToken, oldConfig.DiscordToken);
        await Helpers.SetValue(Keys.DiscordBmjId, oldConfig.DiscordBmjId);
        logger.Info($"{oldConfigPath} migration done.");

        logger.Info("Renaming files no longer in use");
        // Utils.SafelyRenameFile will attempt to rename and swallow any exception (with logging) if it fails
        Utils.SafelyRenameFile(oldConfigPath, $"{oldConfigPath}.migrated");

        logger.Info("File renamed");
    }

    public static async Task MigrateImages()
    {
        var logger = LogManager.GetCurrentClassLogger();
        await using var db = new ApplicationDbContext();
        logger.Info("Migrating images to the database table");
        if (await db.Images.AnyAsync())
        {
            logger.Info("Not continuing as there's already images in the database");
            return;
        }

        var imagesToMigrate = await Helpers.GetMultipleValues([
            Keys.BotGmKasinoImageRotation, Keys.BotGnKasinoImageRotation, Keys.WinmanjackImgUrl, Keys.BotPraygeImgUrl,
            Keys.BotCrackpipeImgUrl
        ]);

        logger.Info("Migrating gmkasino images");
        foreach (var image in imagesToMigrate[Keys.BotGmKasinoImageRotation].JsonDeserialize<List<string>>() ?? [])
        {
            await db.Images.AddAsync(new ImageDbModel {Key = "gmkasino", LastSeen = DateTimeOffset.UtcNow, Url = image});
        }
        
        logger.Info("Migrating gnkasino images");
        foreach (var image in imagesToMigrate[Keys.BotGnKasinoImageRotation].JsonDeserialize<List<string>>() ?? [])
        {
            await db.Images.AddAsync(new ImageDbModel {Key = "gnkasino", LastSeen = DateTimeOffset.UtcNow, Url = image});
        }

        if (imagesToMigrate[Keys.WinmanjackImgUrl].Value != null)
        {
            logger.Info("Migrating winmanjack");
            await db.Images.AddAsync(new ImageDbModel
            {
                Key = "winmanjack", LastSeen = DateTimeOffset.UtcNow,
                Url = imagesToMigrate[Keys.WinmanjackImgUrl].Value!
            });
        }
        
        if (imagesToMigrate[Keys.BotPraygeImgUrl].Value != null)
        {
            logger.Info("Migrating prayge");
            await db.Images.AddAsync(new ImageDbModel
            {
                Key = "prayge", LastSeen = DateTimeOffset.UtcNow,
                Url = imagesToMigrate[Keys.BotPraygeImgUrl].Value!
            });
        }

        if (imagesToMigrate[Keys.BotCrackpipeImgUrl].Value != null)
        {
            logger.Info("Migrating crackpipe");
            await db.Images.AddAsync(new ImageDbModel
            {
                Key = "crackpipe", LastSeen = DateTimeOffset.UtcNow,
                Url = imagesToMigrate[Keys.BotCrackpipeImgUrl].Value!
            });
        }
        
        logger.Info("Adding bassmanjack");
        await db.Images.AddAsync(new ImageDbModel
        {
            Key = "bassmanjack", LastSeen = DateTimeOffset.UtcNow,
            Url = "https://i.postimg.cc/SRstzMQt/boss-soy-koi.gif"
        });
        
        logger.Info("Adding sent");
        await db.Images.AddAsync(new ImageDbModel
        {
            Key = "sent", LastSeen = DateTimeOffset.UtcNow,
            Url = "https://i.ibb.co/GHq7hb1/4373-g-N5-HEH2-Hkc.png"
        });

        logger.Info("Adding helpme");
        await db.Images.AddAsync(new ImageDbModel
        {
            Key = "helpme", LastSeen = DateTimeOffset.UtcNow,
            Url = "https://i.postimg.cc/fTw6tGWZ/ineedmoneydumbfuck.png"
        });

        await db.SaveChangesAsync();
        logger.Info("Image migration complete");
    }
    
    public static List<BuiltInSettingsModel> BuiltInSettings =
    [
        new BuiltInSettingsModel
        {
            Key = Keys.PusherEndpoint,
            Regex = @".+",
            Description =
                "Pusher WebSocket endpoint URL",
            Default = "wss://ws-us2.pusher.com/app/32cbd69e4b950bf97679?protocol=7&client=js&version=7.6.0&flash=false",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KiwiFarmsWsEndpoint,
            Regex = @".+",
            Description =
                "Kiwi Farms chat WebSocket endpoint",
            Default = "wss://kiwifarms.st:9443/chat.ws",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.PusherChannels,
            Regex = @".+",
            Description =
                "List of Pusher channels to subscribe to",
            Default = null,
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Array
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KiwiFarmsRoomId,
            Regex = @"\d+",
            Description =
                "Kiwi Farms Keno Kasino room ID",
            Default = "15",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.Proxy,
            Regex = @".+",
            Description =
                "Proxy to use for all outgoing requests. Null to disable",
            Default = null,
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KiwiFarmsWsReconnectTimeout,
            Regex = @"\d+",
            Description =
                "Kiwi Farms chat reconnect timeout",
            Default = "30",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.PusherReconnectTimeout,
            Regex = @"\d+",
            Description =
                "Pusher reconnect timeout",
            Default = "30",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.GambaSeshDetectEnabled,
            Regex = @"true|false",
            Description =
                "Whether to enable detection for the presence of GambaSesh",
            Default = "true",
            IsSecret = false,
            CacheDuration = TimeSpan.FromMinutes(5),
            ValueType = SettingValueType.Boolean
        },
        new BuiltInSettingsModel
        {
            Key = Keys.GambaSeshUserId,
            Regex = @"\d+",
            Description =
                "GambaSesh's uer ID for the purposes of detection",
            Default = "168162",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KickIcon,
            Regex = @".+",
            Description =
                "Kick Icon to use for relaying chat messages",
            Default = "https://i.postimg.cc/Qtw4nCPG/kick16.png",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KiwiFarmsDomain,
            Regex = @".+",
            Description =
                "Domain to use when retrieving a session token",
            Default = "kiwifarms.st",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KiwiFarmsUsername,
            Regex = @".+",
            Description =
                "Username to use when authenticating with Kiwi Farms",
            Default = null,
            IsSecret = true,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KiwiFarmsPassword,
            Regex = @".+",
            Description =
                "Password to use when authenticating with Kiwi Farms",
            Default = null,
            IsSecret = true,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KiwiFarmsChromiumPath,
            Regex = @".+",
            Description =
                "Path to download the Chromium install used for the token grabber",
            Default = "chromium_install",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.TwitchBossmanJackId,
            Regex = @"\d+",
            Description =
                "BossmanJack's Twitch channel ID",
            Default = "114122847",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.TwitchBossmanJackUsername,
            Regex = @".+",
            Description =
                "BossmanJack's Twitch channel username",
            Default = "thebossmanjack",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KiwiFarmsSuppressChatMessages,
            Regex = @"true|false",
            Description =
                "Enable to prevent messages from actually being sent to chat.",
            Default = "false",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Boolean
        },
        new BuiltInSettingsModel
        {
            Key = Keys.DiscordToken,
            Regex = @".+",
            Description =
                "Token to use when authenticating with Discord. Set to null to disable.",
            Default = null,
            IsSecret = true,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.DiscordBmjId,
            Regex = @"\d+",
            Description =
                "BossmanJack's Discord user ID",
            Default = "554123642246529046",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.TwitchIcon,
            Regex = ".+",
            Description = "URL for the 16px Twitch icon",
            Default = "https://i.postimg.cc/QMFVV2Xk/twitch16.png",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.DiscordIcon,
            Regex = ".+",
            Description = "URL for the 16px Discord icon",
            Default = "https://i.postimg.cc/cLmQrp89/discord16.png",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.ShuffleBmjUsername,
            Regex = ".+",
            Description = "Bossman's Shuffle Username",
            Default = "TheBossmanJack",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.JuiceCooldown,
            Regex = @"\d+",
            Description = "Cooldown (in seconds) until you can get juiced again",
            Default = "3600",
            IsSecret = false,
            CacheDuration = TimeSpan.FromMinutes(5),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.JuiceAmount,
            Regex = @"\d+",
            Description = "Amount of $KKK to juice",
            Default = "50",
            IsSecret = false,
            CacheDuration = TimeSpan.FromMinutes(5),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KickEnabled,
            Regex = "true|false",
            Description = "Whether to enable Kick functionality (Pusher websocket mainly)",
            Default = "true",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Boolean
        },
        new BuiltInSettingsModel
        {
            Key = Keys.HowlggDivisionAmount,
            Regex = @"\d+",
            Description = "How much to divide the Howlgg bets/profit by to get the real value",
            Default = "1650",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel()
        {
            Key = Keys.KiwiFarmsGreenColor,
            Regex = ".+",
            Description = "Green color used for showing positive values in chat",
            Default = "#3dd179",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel()
        {
            Key = Keys.KiwiFarmsRedColor,
            Regex = ".+",
            Description = "Red color used for showing negative values in chat",
            Default = "#f1323e",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel()
        {
            Key = Keys.JackpotBmjUsername,
            Regex = ".+",
            Description = "Bossman's username on Jackpot",
            Default = "TheBossmanJack",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.RainbetBmjPublicIds,
            Regex = ".+",
            Description = "Bossman's rainbet public IDs",
            Default = "[\"Ir04170wLulcjtePCL7P6lmeOlepRaNp\", \"IA9RHFR1NLHL33AVOM9GL2G2CINM9I6P\"]",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Array
        },
        new BuiltInSettingsModel
        {
            Key = Keys.FlareSolverrApiUrl,
            Regex = ".+",
            Description = "URL for your FlareSolverr service API",
            Default = "http://localhost:8191/",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.FlareSolverrProxy,
            Regex = ".+",
            Description = "Proxy in use specifically for FlareSolverr",
            Default = null,
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.ChipsggBmjUserIds,
            Regex = ".+",
            Description = "Bossman's Chips.gg username",
            //Default = "[\"TheBossmanJack\", \"Yabuddy757\"]",
            Default = "[\"1af247cd-67e0-4029-8a93-b8d19c275072\", \"e97ebf3e-d5a8-4583-ab35-5095a05f282e\"]",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Array
        },
        new BuiltInSettingsModel
        {
            Key = Keys.RestreamUrl,
            Regex = ".+",
            Description = "URL for the restream",
            Default = "No URL set",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.HowlggBmjUserId,
            Regex = @"\d+",
            Description = "BMJ's user ID on howl.gg",
            Default = "951905",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KiwiFarmsCookies,
            Regex = ".+",
            Description = "Kiwi Farms cookies in key-value pair format",
            // Empty JSON object as it's a Dictionary<string, string> object
            Default = "{}",
            IsSecret = true,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Complex
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BotGmKasinoImageRotation,
            Regex = ".+",
            Description = "Rotation of images for the !gmkasino command",
            // It's a JSON array
            Default = "[\"https://i.postimg.cc/QMzBRmH7/hiiiii.gif\"]",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Array
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BotGnKasinoImageRotation,
            Regex = ".+",
            Description = "Rotation of images for the !gnkasino command",
            // It's a JSON array
            Default = "[\"https://kiwifarms.st/attachments/sleepyjack-gif.5342620/\"]",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Array
        },
        new BuiltInSettingsModel
        {
            Key = Keys.TwitchShillRestreamOnCommercial,
            Regex = "(true|false)",
            Description = "Whether to shill the ad-free restream on commercial",
            Default = "true",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Boolean
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KiwiFarmsInactivityTimeout,
            Regex = @"\d+",
            // You would think the WS library would trip up with the "NoMessageReceived" exception, but there's some bug
            // where it'll occasionally fail to reconnect properly and sit there dead forever, hence the watchdog timer
            Description = "Length of time the client can go without receiving ANY packets from Sneedchat before forcing a reconnect.",
            Default = "300",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KiwiFarmsPingInterval,
            Regex = @"\d+",
            Description = "Interval in seconds to ping Sneedchat using the non-existent /ping command. " +
                          "Note this affects how often the bot will check inactivity of the connection.",
            Default = "10",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.JuiceLoserDivision,
            Regex = @"\d+",
            Description = "Amount to divide the juice by if the user's rack is Loser",
            Default = "5",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.CrackedZalgoFuckUpMode,
            Regex = @"\d+",
            Description = "FuckUpMode. 0 = Min, 1 = Normal, 2 = Max",
            Default = "1",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.CrackedZalgoFuckUpPosition,
            Regex = @"\d+",
            Description = "FuckUpPosition: 1 = Up, 2 = Middle, 3 = UpAndMiddle, 4 = Bot (Bottom), 5 = UpAndBot, 6 = MiddleAndBot, 7 = All",
            Default = "2",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.WinmanjackImgUrl,
            Regex = ".+",
            Description = "URL for the winmanjack image",
            Default = "https://kiwifarms.st/attachments/winmanjack_bgr-png.6414050/",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BotDisconnectReplayLimit,
            Regex = @"\d+",
            Description = "Limit of messages which could not be sent while bot was disconnected to replay on connect",
            Default = "10",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KiwiFarmsJoinFailLimit,
            Regex = @"\d+",
            Description = "Limit of times to fail joining the room before wiping cookies",
            Default = "2",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KickChannels,
            Regex = ".+",
            Description = "Kick channels the bot knows about for notifications",
            Default = "[]",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Array
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BotPraygeImgUrl,
            Regex = ".+",
            Description = "Image URL for the prayge command",
            Default = "https://uploads.kiwifarms.st/data/attachments/5962/5962565-2485292e69a4ccc23505826f88ecdab1.jpg",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BotCrackpipeImgUrl,
            Regex = ".+",
            Description = "Image URL for the crackpipe command",
            Default = "https://kiwifarms.st/attachments/crack-smoke-gif.6449901/",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BotCleanStartTime,
            Regex = ".+",
            Description = "ISO8601 date of Bossman's sobriety",
            Default = "2024-09-19T13:33:00-04:00",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BotRehabEndTime,
            Regex = ".+",
            Description = "ISO8601 date of Bossman's rehab end",
            Default = "2024-10-24T09:00:00-04:00",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BotPoNextVisit,
            Regex = ".+",
            Description = "ISO8601 date of Bossman's next PO visit",
            Default = "2024-10-18T12:00:00-04:00",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BotNextCourtHearing,
            Regex = ".+",
            Description = "ISO8601 date of Bossman's next court hearing",
            Default = "2024-10-29T09:00:00-04:00",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BotJailStartTime,
            Regex = ".+",
            Description = "ISO8601 date of when Bossman's incarceration began",
            Default = "2024-10-27T03:25:00-05:00",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BotCourtCalendar,
            Regex = ".+",
            Description = "JSON array containing court hearings",
            Default = "[]",
            IsSecret = false,
            CacheDuration = TimeSpan.Zero,
            ValueType = SettingValueType.Complex
        },
        new BuiltInSettingsModel
        {
            Key = Keys.HowlggEnabled,
            Regex = "(true|false)",
            Description = "Whether the Howl.gg integration is enabled at all",
            Default = "false",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Boolean
        },
        new BuiltInSettingsModel
        {
            Key = Keys.ChipsggEnabled,
            Regex = "(true|false)",
            Description = "Whether the Chips.gg integration is enabled at all",
            Default = "false",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Boolean
        },
        new BuiltInSettingsModel
        {
            Key = Keys.RainbetEnabled,
            Regex = "(true|false)",
            Description = "Whether the Rainbet integration is enabled at all",
            Default = "false",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Boolean
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BotImageAcceptableKeys,
            Regex = ".+",
            Description = "List of valid keys for the image rotation feature",
            Default = "[\"gmkasino\", \"gnkasino\", \"winmanjack\", \"prayge\", \"crackpipe\", \"bassmanjack\", \"sent\", \"helpme\"]",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Array
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BotToyStoryImage,
            Regex = ".+",
            Description = "Image to use for the Toy Story joke",
            Default = "https://i.ibb.co/603dk32R/nonce-drop.png",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BotImageRandomSliceDivideBy,
            Regex = @"\d+",
            Description = "What value to divide the image count by for determining how many images to randomly choose from. " +
                          "e.g. a value of 10 on 50 images means the 5 least seen images are chosen from randomly. " +
                          "If the count of images is =< this value, it'll just grab the oldest image. " +
                          "Fractions will be rounded, so a value of 5 with 7 images will round down and take the oldest image.",
            Default = "5",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.TwitchCommercialRestreamShillMessage,
            Regex = ".+",
            Description = "The specific restream to shill when a commercial is detected if shilling is enabled",
            Default = "No commercial restream shill message set",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BotChrisDjLiveImage,
            Regex = ".+",
            Description = "Image that the bot will send when ChrisDJ goes live",
            Default = "https://kiwifarms.st/attachments/nonce-live-png.7015533/",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.DiscordTemporarilyBypassGambaSeshInitialValue,
            Regex = "(true|false)",
            Description = "What the initial value of the Discord GambaSesh temporary bypass variable should be",
            Default = "false",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Boolean
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BotKeesSeen,
            Regex = "(true|false)",
            Description = "Track if Kees has been seen so users can receive a one-time notice if he suddenly shows up",
            Default = "false",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Boolean
        },
        new BuiltInSettingsModel
        {
            Key = Keys.ClashggEnabled,
            Regex = "(true|false)",
            Description = "Whether the Clash.gg integration should be enabled",
            Default = "true",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Boolean
        },
        new BuiltInSettingsModel
        {
            Key = Keys.ClashggBmjIds,
            Regex = ".+",
            Description = "List of IDs that austingambles is using",
            Default = "[]",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Array
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BotAlmanacText,
            Regex = ".+",
            Description = "Text to send when reminding people of the Almanac",
            Default = "Placeholder text for the Almanac",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        },
        new BuiltInSettingsModel
        {
            Key = Keys.BotAlmanacInterval,
            Regex = @"\d+",
            Description = "Interval for Almanac reminders in seconds",
            Default = "14400", // 4 hours
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1),
            ValueType = SettingValueType.Text
        }
    ];
    
    public static class Keys
    {
        public static string PusherEndpoint = "Pusher.Endpoint";
        public static string KiwiFarmsWsEndpoint = "KiwiFarms.WsEndpoint";
        public static string PusherChannels = "Pusher.Channels";
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
        public static string KiwiFarmsChromiumPath = "KiwiFarms.ChromiumPath";
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
        public static string JackpotBmjUsername = "Jackpot.BmjUsername";
        public static string RainbetBmjPublicIds = "Rainbet.BmjPublicIds";
        public static string FlareSolverrApiUrl = "FlareSolverr.ApiUrl";
        public static string FlareSolverrProxy = "FlareSolverr.Proxy";
        public static string ChipsggBmjUserIds = "Chipsgg.BmjUserIds";
        public static string RestreamUrl = "RestreamUrl";
        public static string KiwiFarmsCookies = "KiwiFarms.Cookies";
        public static string BotGmKasinoImageRotation = "Bot.GmKasinoImageRotation";
        public static string BotGnKasinoImageRotation = "Bot.GnKasino.ImageRotation";
        public static string TwitchShillRestreamOnCommercial = "Twitch.ShillRestreamOnCommercial";
        public static string KiwiFarmsInactivityTimeout = "KiwiFarms.InactivityTimeout";
        public static string KiwiFarmsPingInterval = "KiwiFarms.PingInterval";
        public static string JuiceLoserDivision = "Juice.LoserDivision";
        public static string CrackedZalgoFuckUpMode = "Cracked.ZalgoFuckUpMode";
        public static string CrackedZalgoFuckUpPosition = "Cracked.ZalgoFuckUpPosition";
        public static string WinmanjackImgUrl = "Winmanjack.ImgUrl";
        public static string BotDisconnectReplayLimit = "Bot.DisconnectReplayLimit";
        public static string KiwiFarmsJoinFailLimit = "KiwiFarms.JoinFailLimit";
        public static string KickChannels = "Kick.Channels";
        public static string BotPraygeImgUrl = "Bot.Prayge.ImgUrl";
        public static string BotCrackpipeImgUrl = "Bot.Crackpipe.ImgUrl";
        public static string BotCleanStartTime = "Bot.Clean.StartTime";
        public static string BotRehabEndTime = "Bot.Rehab.EndTime";
        public static string BotPoNextVisit = "Bot.Po.NextVisit";
        public static string BotNextCourtHearing = "Bot.Court.NextHearing";
        public static string BotJailStartTime = "Bot.Jail.StartTime";
        public static string BotCourtCalendar = "Bot.Court.Calendar";
        public static string HowlggEnabled = "Howlgg.Enabled";
        public static string ChipsggEnabled = "Chipsgg.Enabled";
        public static string RainbetEnabled = "Rainbet.Enabled";
        public static string BotImageAcceptableKeys = "Bot.Image.AcceptableKeys";
        public static string BotToyStoryImage = "Bot.ToyStoryImage";
        public static string BotImageRandomSliceDivideBy = "Bot.Image.RandomSliceDivideBy";
        public static string TwitchCommercialRestreamShillMessage = "Twitch.CommercialRestreamShillMessage";
        public static string BotChrisDjLiveImage = "Bot.ChrisDjLiveImage";
        public static string DiscordTemporarilyBypassGambaSeshInitialValue =
            "Discord.TemporarilyBypassGambaSeshInitialValue";
        public static string BotKeesSeen = "Bot.KeesSeen";
        public static string ClashggEnabled = "Clashgg.Enabled";
        public static string ClashggBmjIds = "Clashgg.BmjIds";
        public static string BotAlmanacText = "Bot.Almanac.Text";
        public static string BotAlmanacInterval = "Bot.Almanac.Interval";
    }
}