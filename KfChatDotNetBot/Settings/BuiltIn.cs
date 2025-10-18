using System.Diagnostics.CodeAnalysis;
using System.Reflection;
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
        logger.Info("Enumerating through fields in the Keys class");
        var keysType = typeof(Keys);
        var fields = keysType.GetFields();
        logger.Info($"Got {fields.Length} fields");
        foreach (var field in fields)
        {
            var attribute = field.GetCustomAttribute<BuiltInSetting>();
            if (attribute == null)
            {
                logger.Error($"Field {field.Name} does not have the BuiltInSetting attribute!");
                continue;
            }

            if (field.GetValue(null) is not string key)
            {
                logger.Error($"Failed to cast field {field.Name}'s value to string, got a null");
                continue;
            }
            var builtIn = attribute.ToModel(key);
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
    
    public static class Keys
    {
        [BuiltInSetting("Pusher endpoint for Kick", SettingValueType.Text,
            "wss://ws-us2.pusher.com/app/32cbd69e4b950bf97679?protocol=7&client=js&version=7.6.0&flash=false")]
        public static string PusherEndpoint = "Pusher.Endpoint";
        [BuiltInSetting(description: "Kiwi Farms chat WebSocket endpoint", defaultValue: "wss://kiwifarms.st:9443/chat.ws",
            valueType: SettingValueType.Text)]
        public static string KiwiFarmsWsEndpoint = "KiwiFarms.WsEndpoint";
        [BuiltInSetting("Kiwi Farms Room ID for the Keno Kasino", SettingValueType.Text, "15",
            WholeNumberRegex)]
        public static string KiwiFarmsRoomId = "KiwiFarms.RoomId";
        [BuiltInSetting("Proxy to use for all outgoing requests. null to disable", SettingValueType.Text)]
        public static string Proxy = "Proxy";
        [BuiltInSetting("Kiwi Farms chat reconnect timeout", SettingValueType.Text, "30", WholeNumberRegex)]
        public static string KiwiFarmsWsReconnectTimeout = "KiwiFarms.WsReconnectTimeout";
        [BuiltInSetting("Pusher chat reconnect timeout", SettingValueType.Text, "30", WholeNumberRegex)]
        public static string PusherReconnectTimeout = "Pusher.ReconnectTimeout";
        [BuiltInSetting("Whether to enable the detection for the presence of GambaSesh",
            SettingValueType.Boolean, "true", BooleanRegex, cacheDurationSeconds: 300)]
        public static string GambaSeshDetectEnabled = "GambaSesh.DetectEnabled";
        [BuiltInSetting("GambaSesh's user ID for the purposes of detection", SettingValueType.Text, "168162", WholeNumberRegex)]
        public static string GambaSeshUserId = "GambaSesh.UserId";
        [BuiltInSetting("Kick Icon to use for relaying chat messages", SettingValueType.Text,
            "https://i.postimg.cc/Qtw4nCPG/kick16.png")]
        public static string KickIcon = "Kick.Icon";
        [BuiltInSetting("Domain to use when retrieving a session token", SettingValueType.Text, "kiwifarms.st")]
        public static string KiwiFarmsDomain = "KiwiFarms.Domain";
        [BuiltInSetting("Username to use when authentication with Kiwi Farms", SettingValueType.Text)]
        public static string KiwiFarmsUsername = "KiwiFarms.Username";
        [BuiltInSetting("Password to use when authenticating with Kiwi Farms", SettingValueType.Text, isSecret: true)]
        public static string KiwiFarmsPassword = "KiwiFarms.Password";
        [BuiltInSetting("BossmanJack's Twitch channel ID", SettingValueType.Text, "114122847", WholeNumberRegex)]
        public static string TwitchBossmanJackId = "Twitch.BossmanJackId";
        [BuiltInSetting("BossmanJack's Twitch channel username", SettingValueType.Text, "imbossmanjack")]
        public static string TwitchBossmanJackUsername = "Twitch.BossmanJackUsername";
        [BuiltInSetting("Enable to prevent messages from actually being sent to chat.", SettingValueType.Boolean, 
            "false", BooleanRegex)]
        public static string KiwiFarmsSuppressChatMessages = "KiwiFarms.SuppressChatMessages";
        [BuiltInSetting("Token to use when authenticating with Discord. Set to null to disable.", 
            SettingValueType.Text, isSecret: true)]
        public static string DiscordToken = "Discord.Token";
        [BuiltInSetting("BossmanJack's Discord user ID", SettingValueType.Text, "554123642246529046", WholeNumberRegex)]
        public static string DiscordBmjId = "Discord.BmjId";
        [BuiltInSetting("URL for the 16px Twitch icon", SettingValueType.Text, 
            "https://i.postimg.cc/QMFVV2Xk/twitch16.png")]
        public static string TwitchIcon = "Twitch.Icon";
        [BuiltInSetting("URL for the 16px Discord icon", SettingValueType.Text, 
            "https://i.postimg.cc/cLmQrp89/discord16.png")]
        public static string DiscordIcon = "Discord.Icon";
        [BuiltInSetting("Bossman's Shuffle Username", SettingValueType.Text, "TheBossmanJack")]
        public static string ShuffleBmjUsername = "Shuffle.BmjUsername";
        [BuiltInSetting("Bossman's Shuffle.us Username", SettingValueType.Text, "BossmanJack")]
        public static string ShuffleDotUsBmjUsername = "ShuffleDotUs.BmjUsername";
        [BuiltInSetting("Whether to enable Kick functionality (Pusher websocket mainly)", SettingValueType.Boolean,
            "true", BooleanRegex)]
        public static string KickEnabled = "Kick.Enabled";
        [BuiltInSetting("How much to divide the Howlgg bets/profit by to get the real value", SettingValueType.Text,
            "1650", WholeNumberRegex)]
        public static string HowlggDivisionAmount = "Howlgg.DivisionAmount";
        [BuiltInSetting("BMJ's user ID on howl.gg", SettingValueType.Text, "951905", WholeNumberRegex)]
        public static string HowlggBmjUserId = "Howlgg.BmjUserId";
        [BuiltInSetting("Green color used for showing positive values in chat", SettingValueType.Text, 
            "#3dd179")]
        public static string KiwiFarmsGreenColor = "KiwiFarms.GreenColor";
        [BuiltInSetting("Red color used for showing negative values in chat", SettingValueType.Text, 
            "#f1323e")]
        public static string KiwiFarmsRedColor = "KiwiFarms.RedColor";
        [BuiltInSetting("Bossman's usernames on Jackpot", SettingValueType.Array, 
            "[\"TheBossmanJack\", \"Austingambless757\"]")]
        public static string JackpotBmjUsernames = "Jackpot.BmjUsernames";
        [BuiltInSetting("Bossman's rainbet public IDs", SettingValueType.Array, 
            "[\"Ir04170wLulcjtePCL7P6lmeOlepRaNp\", \"IA9RHFR1NLHL33AVOM9GL2G2CINM9I6P\"]")]
        public static string RainbetBmjPublicIds = "Rainbet.BmjPublicIds";
        [BuiltInSetting("URL for your FlareSolverr service API", SettingValueType.Text, 
            "http://localhost:8191/")]
        public static string FlareSolverrApiUrl = "FlareSolverr.ApiUrl";
        [BuiltInSetting("Proxy in use specifically for FlareSolverr", SettingValueType.Text)]
        public static string FlareSolverrProxy = "FlareSolverr.Proxy";
        [BuiltInSetting("Bossman's Chips.gg IDs", SettingValueType.Array, 
            "[\"1af247cd-67e0-4029-8a93-b8d19c275072\", \"e97ebf3e-d5a8-4583-ab35-5095a05f282e\"]")]
        public static string ChipsggBmjUserIds = "Chipsgg.BmjUserIds";
        [BuiltInSetting("URL for the restream", SettingValueType.Text, "No URL set")]
        public static string RestreamUrl = "RestreamUrl";
        [BuiltInSetting("Kiwi Farms cookies in key-value pair format", SettingValueType.Complex, 
            "{}", isSecret: true)]
        public static string KiwiFarmsCookies = "KiwiFarms.Cookies";
        [BuiltInSetting("Length of time the client can go without receiving ANY packets from Sneedchat before " +
                        "forcing a reconnect.", SettingValueType.Text, "300", WholeNumberRegex)]
        public static string KiwiFarmsInactivityTimeout = "KiwiFarms.InactivityTimeout";
        [BuiltInSetting("Interval in seconds to ping Sneedchat using the non-existent /ping command. " +
                        "Note this affects how often the bot will check inactivity of the connection", SettingValueType.Text,
            "10", WholeNumberRegex)]
        public static string KiwiFarmsPingInterval = "KiwiFarms.PingInterval";
        [BuiltInSetting("FuckUpMode. 0 = Min, 1 = Normal, 2 = Max", SettingValueType.Text, 
            "1", WholeNumberRegex)]
        public static string CrackedZalgoFuckUpMode = "Cracked.ZalgoFuckUpMode";
        [BuiltInSetting("FuckUpPosition: 1 = Up, 2 = Middle, 3 = UpAndMiddle, 4 = Bot (Bottom), 5 = UpAndBot, " +
                        "6 = MiddleAndBot, 7 = All",
            SettingValueType.Text, "2", WholeNumberRegex)]
        public static string CrackedZalgoFuckUpPosition = "Cracked.ZalgoFuckUpPosition";
        [BuiltInSetting("Limit of messages which could not be sent while bot was disconnect to replay on connect",
            SettingValueType.Text, "10", WholeNumberRegex)]
        public static string BotDisconnectReplayLimit = "Bot.DisconnectReplayLimit";
        [BuiltInSetting("Limit of times to fail joining the room before wiping cookies", SettingValueType.Text,
            "2", WholeNumberRegex)]
        public static string KiwiFarmsJoinFailLimit = "KiwiFarms.JoinFailLimit";
        [BuiltInSetting("ISO8601 date of Bossman's sobriety", SettingValueType.Text,
            "2024-09-19T13:33:00-04:00")]
        public static string BotCleanStartTime = "Bot.Clean.StartTime";
        [BuiltInSetting("ISO8601 date of Bossman's rehab end", SettingValueType.Text,
            "2024-10-24T09:00:00-04:00")]
        public static string BotRehabEndTime = "Bot.Rehab.EndTime";
        [BuiltInSetting("ISO8601 date of Bossman's next PO visit", SettingValueType.Text, 
            "ISO8601 date of Bossman's next PO visit")]
        public static string BotPoNextVisit = "Bot.Po.NextVisit";
        [BuiltInSetting("ISO8601 date of when Bossman's incarceration began", SettingValueType.Text, 
            "2024-10-27T03:25:00-05:00")]
        public static string BotJailStartTime = "Bot.Jail.StartTime";
        [BuiltInSetting("JSON array containing court hearings", SettingValueType.Complex, "[]", cacheDurationSeconds: 0)]
        public static string BotCourtCalendar = "Bot.Court.Calendar";
        [BuiltInSetting("Whether the Howl.gg integration is enabled at all", SettingValueType.Boolean, "false", BooleanRegex)]
        public static string HowlggEnabled = "Howlgg.Enabled";
        [BuiltInSetting("Whether the Chips.gg integration is enabled at all", SettingValueType.Boolean, "false", BooleanRegex)]
        public static string ChipsggEnabled = "Chipsgg.Enabled";
        [BuiltInSetting("Whether the Rainbet integration is enabled at all", SettingValueType.Boolean, "false", BooleanRegex)]
        public static string RainbetEnabled = "Rainbet.Enabled";
        [BuiltInSetting("List of valid keys for the image rotation feature", SettingValueType.Array, "[]")]
        public static string BotImageAcceptableKeys = "Bot.Image.AcceptableKeys";
        [BuiltInSetting("What value to divide the image count by for determining how many images to randomly choose from. " +
                        "e.g. a value of 10 on 50 images means the 5 least seen images are chosen from randomly. " +
                        "If the count of images is =< this value, it'll just grab the oldest image. " +
                        "Fractions will be rounded, so a value of 5 with 7 images will round down and take the oldest image.",
            SettingValueType.Text, "5", WholeNumberRegex)]
        public static string BotImageRandomSliceDivideBy = "Bot.Image.RandomSliceDivideBy";
        [BuiltInSetting("What the initial value of the Discord GambaSesh temporary bypass variable should be", 
            SettingValueType.Boolean, "false", BooleanRegex)]
        public static string DiscordTemporarilyBypassGambaSeshInitialValue =
            "Discord.TemporarilyBypassGambaSeshInitialValue";
        // I miss you. Please come back.
        [BuiltInSetting("Track if Kees has been seen so users can receive a one-time notice if he suddenly shows up", 
            SettingValueType.Boolean, "false", BooleanRegex)]
        public static string BotKeesSeen = "Bot.KeesSeen";
        [BuiltInSetting("Whether the Clash.gg integration should be enabled", SettingValueType.Boolean, "true", BooleanRegex)]
        public static string ClashggEnabled = "Clashgg.Enabled";
        [BuiltInSetting("List of IDs that BossmanJack is using on Clash.gg", SettingValueType.Array, "[]")]
        public static string ClashggBmjIds = "Clashgg.BmjIds";
        [BuiltInSetting("Text to send when reminding people of the Almanac", SettingValueType.Text, 
            "Placeholder text for the Almanac")]
        public static string BotAlmanacText = "Bot.Almanac.Text";
        [BuiltInSetting("Interval for Almanac reminders in seconds", SettingValueType.Text, "14400", WholeNumberRegex)]
        public static string BotAlmanacInterval = "Bot.Almanac.Interval";
        [BuiltInSetting("Initial state of the Almanac reminder", SettingValueType.Boolean, "false", BooleanRegex)]
        public static string BotAlmanacInitialState = "Bot.Almanac.InitialState";
        [BuiltInSetting("Whether the pigcube should self destruct after a random interval", SettingValueType.Boolean, "true", BooleanRegex)]
        public static string BotImagePigCubeSelfDestruct = "Bot.Image.PigCubeSelfDestruct";
        [BuiltInSetting("URL of the inverted pig cube for the special deletion logic", SettingValueType.Text,
            "https://kiwifarms.st/attachments/7226614-185d31e0b73350f2765b8051121a05d2-webp.7271720/")]
        public static string BotImageInvertedCubeUrl = "Bot.Image.InvertedCubeUrl";
        [BuiltInSetting("Min value for the Pig Cube self destruct Random.Next() in milliseconds", SettingValueType.Text, "5000", WholeNumberRegex)]
        public static string BotImagePigCubeSelfDestructMin = "Bot.Image.PigCubeSelfDestructMin";
        [BuiltInSetting("Max value for the Pig Cube self destruct Random.Next() in milliseconds", SettingValueType.Text, "15000", WholeNumberRegex)]
        public static string BotImagePigCubeSelfDestructMax = "Bot.Image.PigCubeSelfDestructMax";
        [BuiltInSetting("Value in milliseconds for how long the bot should wait before self destructing the inverted pig cube",
            SettingValueType.Text, "5000", WholeNumberRegex)]
        public static string BotImageInvertedPigCubeSelfDestructDelay = "Bot.Image.InvertedPigCubeSelfDestructDelay";
        [BuiltInSetting("Whether to enable the BetBolt bet feed tracking", SettingValueType.Boolean, "true",  BooleanRegex)]
        public static string BetBoltEnabled = "BetBolt.Enabled";
        [BuiltInSetting("Austin's usernames on BetBolt", SettingValueType.Array, "[\"AustinGambles\"]")]
        public static string BetBoltBmjUsernames = "BetBolt.BmjUsernames";
        [BuiltInSetting("Whether to enable the Yeet bet feed tracking", SettingValueType.Boolean, "true", BooleanRegex)]
        public static string YeetEnabled = "Yeet.Enabled";
        [BuiltInSetting("Austin's usernames on Yeet", SettingValueType.Array, "[\"Bossmanjack\"]")]
        public static string YeetBmjUsernames = "Yeet.BmjUsernames";
        [BuiltInSetting("Proxy to use for Yeet", SettingValueType.Text, "socks5://ca-van-wg-socks5-301.relays.mullvad.net:1080")]
        public static string YeetProxy = "Yeet.Proxy";
        [BuiltInSetting("Cooldown in seconds for the mom command, 0 to disable", SettingValueType.Text, "30", WholeNumberRegex)]
        public static string MomCooldown = "Mom.Cooldown";
        [BuiltInSetting("Output format to pass to yt-dlp using -o", SettingValueType.Text, 
            "%(title)s - %(uploader)s [%(id)s] %(upload_date)s %(timestamp)s.mp4")]
        public static string CaptureYtDlpOutputFormat = "Capture.YtDlp.OutputFormat";
        [BuiltInSetting("Working directory set when running yt-dlp", SettingValueType.Text, "/tmp/discord/")]
        public static string CaptureYtDlpWorkingDirectory = "Capture.YtDlp.WorkingDirectory";
        [BuiltInSetting("Path of the yt-dlp binary", SettingValueType.Text, "/usr/local/bin/yt-dlp")]
        public static string CaptureYtDlpBinaryPath = "Capture.YtDlp.BinaryPath";
        [BuiltInSetting("User-Agent that gets passed to yt-dlp --user-agent", SettingValueType.Text, 
            "Mozilla/5.0 (X11; Linux x86_64; rv:128.0) Gecko/20100101 Firefox/128.0")]
        public static string CaptureYtDlpUserAgent = "Capture.YtDlp.UserAgent";
        [BuiltInSetting("What browser to pass to yt-dlp using --cookies-from-browser", SettingValueType.Text, "firefox")]
        public static string CaptureYtDlpCookiesFromBrowser = "Capture.YtDlp.CookiesFromBrowser";
        [BuiltInSetting("Whether the auto-capture system is enabled", SettingValueType.Boolean, "true", BooleanRegex)]
        public static string CaptureEnabled = "Capture.Enabled";
        [BuiltInSetting("Parent terminal to launch the capture inside of, e.g. xfce4-terminal, mate-terminal, etc. " +
                        "Terminal must support -x. The process detaches from the bot so the bot can be freely restarted while a capture is running. " +
                        "Not supported on Windows, the bot will use cmd /C + START on Windows.", 
            SettingValueType.Text, "/usr/bin/mate-terminal")]
        public static string CaptureYtDlpParentTerminal = "Capture.YtDlp.ParentTerminal";
        [BuiltInSetting("Path to store the temporary .sh script used to initiate the capture", SettingValueType.Text, "/tmp/")]
        public static string CaptureYtDlpScriptPath = "Capture.YtDlp.ScriptPath";
        [BuiltInSetting("Whether the Parti stream notification service is enabled", SettingValueType.Boolean,
            "true", BooleanRegex)]
        public static string PartiEnabled = "Parti.Enabled";
        [BuiltInSetting("Path of the streamlink binary", SettingValueType.Text, "/usr/local/bin/streamlink")]
        public static string CaptureStreamlinkBinaryPath = "Capture.Streamlink.BinaryPath";
        [BuiltInSetting("Output format to pass to streamlink using --output", SettingValueType.Text, 
            "{author}-{id}-{title}-{time:%Y-%m-%d_%Hh%Mm%Ss}.ts")]
        public static string CaptureStreamlinkOutputFormat = "Capture.Streamlink.OutputFormat";
        [BuiltInSetting("Path of the remux script used to convert .ts to .mp4", SettingValueType.Text, 
            "pwsh /root/BMJ/Convert-TsToMp4.ps1")]
        public static string CaptureStreamlinkRemuxScript = "Capture.Streamlink.RemuxScript";
        [BuiltInSetting("Special options for Twitch streams captured with Streamlink", SettingValueType.Text, 
            "--twitch-disable-ad --twitch-proxy-playlist=https://eu.luminous.dev," +
            "https://eu2.luminous.dev,https://as.luminous.dev,https://cdn.perfprod.com")]
        public static string CaptureStreamlinkTwitchOptions = "Capture.Streamlink.TwitchOptions";
        [BuiltInSetting("How often (in seconds) to check if a DLive streamer is live", 
            SettingValueType.Text, "15", WholeNumberRegex)]
        public static string DLiveCheckInterval = "DLive.CheckInterval";
        // Setting was originally Complex but this was a mistake, it's just an array of usernames. Ditto for PeerTube
        [BuiltInSetting("Array of DLive streamers who are currently live for persistence between bot restarts",
            SettingValueType.Array, "[]")]
        public static string DLivePersistedCurrentlyLiveStreams = "DLive.PersistedCurrentlyLiveStreams";
        [BuiltInSetting("Array of Kiwi PeerTube stream GUIDs which are currently live for persistence between " +
                        "bot restarts", SettingValueType.Array, "[]")]
        public static string KiwiPeerTubePersistedCurrentlyLiveStreams = "KiwiPeerTube.PersistedCurrentlyLiveStreams";
        [BuiltInSetting("Whether the Kiwi PeerTube live notification is enabled", SettingValueType.Boolean, "true", BooleanRegex)]
        public static string KiwiPeerTubeEnabled = "KiwiPeerTube.Enabled";
        [BuiltInSetting("Interval (in seconds) to check live streams", SettingValueType.Text, "10", WholeNumberRegex)]
        public static string KiwiPeerTubeCheckInterval = "KiwiPeerTube.CheckInterval";
        [BuiltInSetting("Whether to enforce the use of a whitelist (i.e. streamer must be in teh database)", 
            SettingValueType.Boolean, "false", BooleanRegex)]
        public static string KiwiPeerTubeEnforceWhitelist = "KiwiPeerTube.EnforceWhitelist";
        [BuiltInSetting("Interval in seconds for checking if the bot is completely dead", SettingValueType.Text, "15", WholeNumberRegex)]
        public static string BotDeadBotDetectionInterval = "Bot.DeadBotDetectionInterval";
        [BuiltInSetting("Whether the bot should exit if it's dead and unrecoverable", SettingValueType.Boolean, BooleanRegex)]
        public static string BotExitOnDeath = "Bot.ExitOnDeath";
        [BuiltInSetting("Whether to respond to Bossman impersonation", SettingValueType.Boolean, "true", BooleanRegex)]
        public static string BotRespondToDiscordImpersonation = "Bot.RespondToDiscordImpersonation";
        [BuiltInSetting("What is the symbol of the bot's currency when used as a suffix for an amount", 
            SettingValueType.Text, "KKK")]
        public static string MoneySymbolSuffix = "Money.SymbolSuffix";
        [BuiltInSetting("What is the symbol of the bot's currency when used as a prefix for an amount", 
            SettingValueType.Text, "KKK$")]
        public static string MoneySymbolPrefix = "Money.SymbolPrefix";
        [BuiltInSetting("Whether the monetary system is enabled at all", SettingValueType.Boolean, "false", BooleanRegex)]
        public static string MoneyEnabled = "Money.Enabled";
        [BuiltInSetting("Gambler's initial balance on creation", SettingValueType.Text, "100", WholeNumberRegex)]
        public static string MoneyInitialBalance = "Money.InitialBalance";
        [BuiltInSetting("Interval in seconds to check if Bossman is live on Twitch using GraphQL", SettingValueType.Text, "10", WholeNumberRegex)]
        public static string TwitchGraphQlCheckInterval = "TwitchGraphQl.CheckInterval";
        [BuiltInSetting("Whether BossmanJack is currently live on Twitch", SettingValueType.Boolean, "false", BooleanRegex)]
        public static string TwitchGraphQlPersistedCurrentlyLive = "TwitchGraphQl.PersistedCurrentlyLive";
        [BuiltInSetting("Interval in seconds to check if someone is live on Owncast", SettingValueType.Text, "5", WholeNumberRegex)]
        public static string OwncastCheckInterval = "Owncast.CheckInterval";
        [BuiltInSetting("Whether someone is live on Owncast", SettingValueType.Boolean, "false", BooleanRegex)]
        public static string OwncastPersistedCurrentlyLive = "Owncast.PersistedCurrentlyLive";
        [BuiltInSetting("Percentage of total wagered amount should be returned for rakeback", SettingValueType.Text, "2.5")]
        public static string MoneyRakebackPercentage = "Money.Rakeback.Percentage";
        [BuiltInSetting("Minimum rakeback to pay out. Anything below this willt ell the user to go away", SettingValueType.Text, "10", WholeNumberRegex)]
        public static string MoneyRakebackMinimumAmount = "Money.Rakeback.MinimumAmount";
        [BuiltInSetting("Percentage of total amount lost that should be returned for lossback", SettingValueType.Text, "5")]
        public static string MoneyLossbackPercentage = "Money.Lossback.Percentage";
        [BuiltInSetting("Minimum lossback to pay out. Anything below this will tell the user to go away", SettingValueType.Text, "100", WholeNumberRegex)]
        public static string MoneyLossbackMinimumAmount = "Money.Lossback.MinimumAmount";
        [BuiltInSetting("Whether the chink photo should self destruct after being sent", SettingValueType.Boolean, "true", BooleanRegex)]
        public static string BotImageChinkSelfDestruct = "Bot.Image.ChinkSelfDestruct";
        [BuiltInSetting("Delay in milliseconds before destrying the chink image", SettingValueType.Text, "7500", WholeNumberRegex)]
        public static string BotImageChinkSelfDestructDelay = "Bot.Image.ChinkSelfDestructDelay";
        [BuiltInSetting("Delay in milliseconds before removing a cooldown message set to auto delete", SettingValueType.Text, "15000", WholeNumberRegex)]
        public static string BotRateLimitCooldownAutoDeleteDelay = "Bot.RateLimit.CooldownAutoDeleteDelay";
        [BuiltInSetting("How often to cleanup expired rate limit entries in seconds", SettingValueType.Text, "300", WholeNumberRegex)]
        public static string BotRateLimitExpiredEntryCleanupInterval = "Bot.RateLimit.ExpiredEntryCleanupInterval";
        [BuiltInSetting("Working directory for BMJ's Twitch streams captured with streamlink", SettingValueType.Text, "/root/twitch/")]
        public static string CaptureStreamlinkBmjWorkingDirectory = "Bot.Streamlink.BmjWorkingDirectory";
        [BuiltInSetting("Only send Discord conversation summaries in the chat where BMJ's Discord ID is listed as participating", SettingValueType.Boolean, "true", BooleanRegex)]
        public static string DiscordOnlySendSummariesIncludingBmj = "Discord.OnlySendSummariesIncludingBmj";
        [BuiltInSetting("Object containing details of Bossman's last sighting", SettingValueType.Complex, "{\n    \"When\": \"2025-10-03T01:20:00-04:00\",\n    \"Activity\": \"going to jail\"\n}")]
        public static string BossmanLastSighting = "Bot.BossmanLastSighting";
        [BuiltInSetting("Delay in milliseconds between each frame on the keno board", SettingValueType.Text, "250", WholeNumberRegex)]
        public static string KasinoKenoFrameDelay = "Kasino.Keno.FrameDelay";
        [BuiltInSetting("Delay in milliseconds before cleaning up the guess what result", SettingValueType.Text, "15000", WholeNumberRegex)]
        public static string KasinoGuessWhatNumberCleanupDelay = "Kasino.GuessWhatNumber.CleanupDelay";
        [BuiltInSetting("Delay in milliseconds before cleaning up the Keno board", SettingValueType.Text, "30000", WholeNumberRegex)]
        public static string KasinoKenoCleanupDelay = "Kasino.Keno.CleanupDelay";
        [BuiltInSetting("Delay in milliseconds before cleaning up the Planes board and result", SettingValueType.Text, "60000", WholeNumberRegex)]
        public static string KasinoPlanesCleanupDelay = "Kasino.Planes.CleanupDelay";
        [BuiltInSetting("Delay in milliseconds between each check to see whether tehre's messages to be deleted", SettingValueType.Text, "1000", WholeNumberRegex)]
        public static string BotScheduledDeletionInterval = "Bot.ScheduledDeletionInterval";
        [BuiltInSetting("Disable the conversation summaries feature", SettingValueType.Boolean, "false", BooleanRegex)]
        public static string DiscordDisableConversationSummaries = "Discord.DisableConversationSummaries";
        [BuiltInSetting("Whether targeted individuals will have their planes endlessly felted", SettingValueType.Boolean, "true", BooleanRegex)]
        public static string KasinoPlanesTargetedRiggeryEnabled = "Kasino.Planes.TargetedRiggeryEnabled";
        [BuiltInSetting("Whether random riggery which affects all users is enabled", SettingValueType.Boolean, "true", BooleanRegex)]
        public static string KasinoPlanesRandomRiggeryEnabled = "Kasino.Planes.RandomRiggeryEnabled";
        [BuiltInSetting("Array of forum IDs to guarantee riggery in Planes", SettingValueType.Array, "[]")]
        public static string KasinoPlanesTargetedRiggeryVictims = "Kasino.Planes.TargetedRiggeryVictims";
    }
}

/// <summary>
/// Attribute for keys that reference built in settings
/// </summary>
/// <param name="description">Description of the setting itself</param>
/// <param name="valueType">What type of value does this setting have</param>
/// <param name="defaultValue">What's the default value of the setting</param>
/// <param name="regex">What regex should be used to validate a new value is correct whenever manipulated through the
/// (TBC) admin settings interface</param>
/// <param name="isSecret">Is the value a secret which should be hidden from log outputs and never revealed to anyone</param>
/// <param name="cacheDurationSeconds">How long should the value be cached for (in seconds)</param>
[AttributeUsage(AttributeTargets.Field)]
[method: SetsRequiredMembers]
public class BuiltInSetting(
    string description,
    SettingValueType valueType,
    string? defaultValue = null,
    string regex = ".+",
    bool isSecret = false,
    int cacheDurationSeconds = 3600)
    : Attribute
{
    public required string Description { get; set; } = description;
    public string? Default { get; set; } = defaultValue;
    public required SettingValueType ValueType { get; set; } = valueType;
    public required string Regex { get; set; } = regex;
    public required bool IsSecret { get; set; } = isSecret;
    public required int CacheDuration { get; set; } = cacheDurationSeconds;

    // Have to pass in a key because C# is rigged and doesn't let you easily access the field the attribute is used on
    // unless you want to nigger rig it with a bunch of reflection which is honestly too much effort
    // ToModel is only used for syncing built in settings and it should know what key it is looking at
    public BuiltInSettingsModel ToModel(string key)
    {
        return new BuiltInSettingsModel
        {
            Key = key,
            Regex = Regex,
            Description = Description,
            Default = Default,
            IsSecret = IsSecret,
            CacheDuration = TimeSpan.FromSeconds(CacheDuration),
            ValueType = ValueType
        };
    }
}
