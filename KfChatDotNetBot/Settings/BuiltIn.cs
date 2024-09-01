using System.Text.Json;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
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
                    CacheDuration = builtIn.CacheDuration.TotalSeconds
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
            CacheDuration = TimeSpan.FromHours(1)
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KiwiFarmsWsEndpoint,
            Regex = @".+",
            Description =
                "Kiwi Farms chat WebSocket endpoint",
            Default = "wss://kiwifarms.st:9443/chat.ws",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1)
        },
        new BuiltInSettingsModel
        {
            Key = Keys.PusherChannels,
            Regex = @".+",
            Description =
                "List of Pusher channels to subscribe to",
            Default = null,
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1)
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KiwiFarmsRoomId,
            Regex = @"\d+",
            Description =
                "Kiwi Farms Keno Kasino room ID",
            Default = "15",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1)
        },
        new BuiltInSettingsModel
        {
            Key = Keys.Proxy,
            Regex = @".+",
            Description =
                "Proxy to use for all outgoing requests. Null to disable",
            Default = null,
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1)
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KiwiFarmsWsReconnectTimeout,
            Regex = @"\d+",
            Description =
                "Kiwi Farms chat reconnect timeout",
            Default = "30",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1)
        },
        new BuiltInSettingsModel
        {
            Key = Keys.PusherReconnectTimeout,
            Regex = @"\d+",
            Description =
                "Pusher reconnect timeout",
            Default = "30",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1)
        },
        new BuiltInSettingsModel
        {
            Key = Keys.GambaSeshDetectEnabled,
            Regex = @"true|false",
            Description =
                "Whether to enable detection for the presence of GambaSesh",
            Default = "true",
            IsSecret = false,
            CacheDuration = TimeSpan.FromMinutes(5)
        },
        new BuiltInSettingsModel
        {
            Key = Keys.GambaSeshUserId,
            Regex = @"\d+",
            Description =
                "GambaSesh's uer ID for the purposes of detection",
            Default = "168162",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1)
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KickIcon,
            Regex = @".+",
            Description =
                "Kick Icon to use for relaying chat messages",
            Default = "https://i.postimg.cc/Qtw4nCPG/kick16.png",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1)
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KiwiFarmsDomain,
            Regex = @".+",
            Description =
                "Domain to use when retrieving a session token",
            Default = "kiwifarms.st",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1)
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KiwiFarmsUsername,
            Regex = @".+",
            Description =
                "Username to use when authenticating with Kiwi Farms",
            Default = null,
            IsSecret = true,
            CacheDuration = TimeSpan.FromHours(1)
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KiwiFarmsPassword,
            Regex = @".+",
            Description =
                "Password to use when authenticating with Kiwi Farms",
            Default = null,
            IsSecret = true,
            CacheDuration = TimeSpan.FromHours(1)
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KiwiFarmsChromiumPath,
            Regex = @".+",
            Description =
                "Path to download the Chromium install used for the token grabber",
            Default = "chromium_install",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1)
        },
        new BuiltInSettingsModel
        {
            Key = Keys.TwitchBossmanJackId,
            Regex = @"\d+",
            Description =
                "BossmanJack's Twitch channel ID",
            Default = "114122847",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1)
        },
        new BuiltInSettingsModel
        {
            Key = Keys.TwitchBossmanJackUsername,
            Regex = @".+",
            Description =
                "BossmanJack's Twitch channel username",
            Default = "thebossmanjack",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1)
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KiwiFarmsSuppressChatMessages,
            Regex = @"true|false",
            Description =
                "Enable to prevent messages from actually being sent to chat.",
            Default = "false",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1)
        },
        new BuiltInSettingsModel
        {
            Key = Keys.DiscordToken,
            Regex = @".+",
            Description =
                "Token to use when authenticating with Discord. Set to null to disable.",
            Default = null,
            IsSecret = true,
            CacheDuration = TimeSpan.FromHours(1)
        },
        new BuiltInSettingsModel
        {
            Key = Keys.DiscordBmjId,
            Regex = @"\d+",
            Description =
                "BossmanJack's Discord user ID",
            Default = "554123642246529046",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1)
        },
        new BuiltInSettingsModel
        {
            Key = Keys.TwitchIcon,
            Regex = ".+",
            Description = "URL for the 16px Twitch icon",
            Default = "https://i.postimg.cc/QMFVV2Xk/twitch16.png",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1)
        },
        new BuiltInSettingsModel
        {
            Key = Keys.DiscordIcon,
            Regex = ".+",
            Description = "URL for the 16px Discord icon",
            Default = "https://i.postimg.cc/cLmQrp89/discord16.png",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1)
        },
        new BuiltInSettingsModel
        {
            Key = Keys.ShuffleBmjUsername,
            Regex = ".+",
            Description = "Bossman's Shuffle Username",
            Default = "TheBossmanJack",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1)
        },
        new BuiltInSettingsModel
        {
            Key = Keys.JuiceCooldown,
            Regex = @"\d+",
            Description = "Cooldown (in seconds) until you can get juiced again",
            Default = "3600",
            IsSecret = false,
            CacheDuration = TimeSpan.FromMinutes(5)
        },
        new BuiltInSettingsModel
        {
            Key = Keys.JuiceAmount,
            Regex = @"\d+",
            Description = "Amount of $KKK to juice",
            Default = "50",
            IsSecret = false,
            CacheDuration = TimeSpan.FromMinutes(5)
        },
        new BuiltInSettingsModel
        {
            Key = Keys.KickEnabled,
            Regex = "true|false",
            Description = "Whether to enable Kick functionality (Pusher websocket mainly)",
            Default = "true",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1)
        },
        new BuiltInSettingsModel
        {
            Key = Keys.HowlggDivisionAmount,
            Regex = @"\d+",
            Description = "How much to divide the Howlgg bets/profit by to get the real value",
            Default = "1650",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1)
        },
        new BuiltInSettingsModel()
        {
            Key = Keys.KiwiFarmsGreenColor,
            Regex = ".+",
            Description = "Green color used for showing positive values in chat",
            Default = "#3dd179",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1)
        },
        new BuiltInSettingsModel()
        {
            Key = Keys.KiwiFarmsRedColor,
            Regex = ".+",
            Description = "Red color used for showing negative values in chat",
            Default = "#f1323e",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1)
        },
        new BuiltInSettingsModel()
        {
            Key = Keys.JackpotBmjUsername,
            Regex = ".+",
            Description = "Bossman's username on Jackpot",
            Default = "TheBossmanJack",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1)
        },
        new BuiltInSettingsModel
        {
            Key = Keys.RainbetBmjPublicId,
            Regex = ".+",
            Description = "Bossman's rainbet public ID",
            Default = "Ir04170wLulcjtePCL7P6lmeOlepRaNp",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1)
        },
        new BuiltInSettingsModel
        {
            Key = Keys.FlareSolverrApiUrl,
            Regex = ".+",
            Description = "URL for your FlareSolverr service API",
            Default = "http://localhost:8191/",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1)
        },
        new BuiltInSettingsModel
        {
            Key = Keys.FlareSolverrProxy,
            Regex = ".+",
            Description = "Proxy in use specifically for FlareSolverr",
            Default = null,
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1)
        },
        new BuiltInSettingsModel
        {
            Key = Keys.ChipsggBmjUsername,
            Regex = ".+",
            Description = "Bossman's Chips.gg username",
            Default = "TheBossmanJack",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1)
        },
        new BuiltInSettingsModel
        {
            Key = Keys.RestreamUrl,
            Regex = ".+",
            Description = "URL for the restream",
            Default = "No URL set",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1)
        },
        new BuiltInSettingsModel
        {
            Key = Keys.HowlggBmjUserId,
            Regex = @"\d+",
            Description = "BMJ's user ID on howl.gg",
            Default = "951905",
            IsSecret = false,
            CacheDuration = TimeSpan.FromHours(1)
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
        public static string KiwiFarmsToken = "KiwiFarms.Token";
        public static string KickEnabled = "Kick.Enabled";
        public static string HowlggDivisionAmount = "Howlgg.DivisionAmount";
        public static string HowlggBmjUserId = "Howlgg.BmjUserId";
        public static string KiwiFarmsGreenColor = "KiwiFarms.GreenColor";
        public static string KiwiFarmsRedColor = "KiwiFarms.RedColor";
        public static string JackpotBmjUsername = "Jackpot.BmjUsername";
        public static string RainbetBmjPublicId = "Rainbet.BmjPublicId";
        public static string FlareSolverrApiUrl = "FlareSolverr.ApiUrl";
        public static string FlareSolverrProxy = "FlareSolverr.Proxy";
        public static string ChipsggBmjUsername = "Chipsgg.BmjUsername";
        public static string RestreamUrl = "RestreamUrl";
    }
}