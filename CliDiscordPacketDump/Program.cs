// This new template sucks

using NLog;

var logger = LogManager.GetCurrentClassLogger();
logger.Info("Starting up");
var token = "authorization token!";
var proxy = "socks5://whatever:1080";
var discord = new KfChatDotNetBot.Services.DiscordService(token, proxy);
discord.StartWsClient().Wait();
logger.Info("Started");
var exitEvent = new ManualResetEvent(false);
exitEvent.WaitOne();