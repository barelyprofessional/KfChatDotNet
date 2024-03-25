using System.Net;
using System.Text;
using NLog;

namespace KfChatDotNetKickBot
{
    public class Program
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            new KickBot();
        }
    }
}