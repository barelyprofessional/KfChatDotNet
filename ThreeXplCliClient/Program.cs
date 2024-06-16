using System.Net;
using System.Text;
using Spectre.Console;
using ThreeXplWsClient.Events;

namespace ThreeXplCliClient
{
    public class Program
    {
        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            AnsiConsole.MarkupLine("[green]3xpl test client started[/]");
            var cliClient = new ThreeXplClient();
            await cliClient.Start();
        }
    }
}