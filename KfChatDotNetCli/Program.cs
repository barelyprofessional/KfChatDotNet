using System.Net;
using System.Text;
using CommandLine;
using NLog;

namespace KfChatDotNetCli
{
    public class Program
    {
        public class Options
        {
            [Option('t', "token", Required = false, Default = null, HelpText = "XF session token from the 'xf_session' cookie")]
            public string XfSessionToken { get; set; } = null!;

            [Option("debug", Required = false, Default = false, HelpText = "Enable debug logging")]
            public bool Debug { get; set; }
            [Option('r', "room", Required = false, Default = int.MaxValue, HelpText = "Room ID to join on start")]
            public int RoomId { get; set; }
        }
        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Parser.Default.ParseArguments<Options>(args).WithParsed(CliOptions);
        }

        static void CliOptions(Options options)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls13;
            if (options.Debug)
            {
                foreach (var rule in LogManager.Configuration.LoggingRules)
                {
                    rule.EnableLoggingForLevel(LogLevel.Debug);
                }
            }

            new ChatCliMain(options.XfSessionToken, options.RoomId);
        }
    }
}
