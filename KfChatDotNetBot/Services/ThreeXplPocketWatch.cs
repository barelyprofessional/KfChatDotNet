using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KfChatDotNetBot.Models;
using NLog;

namespace KfChatDotNetBot.Services;

public class ThreeXplPocketWatch
{
    private Logger _logger = LogManager.GetCurrentClassLogger();
    private string _3xplToken = "3A0_t3st3xplor3rpub11cb3t4efcd21748a5e";
    private string? _proxy;
    private List<PocketWatchModel> _addresses = [];
    private CancellationToken _cancellationToken = CancellationToken.None;
    public delegate void OnPocketWatchEventHandler(object sender, PocketWatchEventModel e);
    public event OnPocketWatchEventHandler OnPocketWatchEvent;

    public ThreeXplPocketWatch(string? proxy = null, CancellationToken? cancellationToken = null)
    {
        _logger.Info("Starting the pocket watch");
        _proxy = proxy;
        if (cancellationToken != null) _cancellationToken = cancellationToken.Value;
    }

    private async Task CheckAddress(PocketWatchModel addy)
    {
        _logger.Debug($"Getting data for {addy.Network}/{addy.Address}");
        var data = await GetAddress(addy.Network, addy.Address);
        _logger.Debug("Received following data");
        _logger.Debug(data.GetRawText);
        var events = data.GetProperty("events").Deserialize<Dictionary<string, List<ThreeXplEventModel>>>();
        if (events == null) throw new InvalidOperationException();
        foreach (var chain in events.Keys)
        {
            
        }
    }

    public async Task<JsonElement> GetAddress(string network, string address)
    {
        var url =
            $"https://api.3xpl.com/{network}/address/{address}?data=address,balances,events,mempool&from=all&token=3A0_t3st3xplor3rpub11cb3t4efcd21748a5e&library=currencies,rates(usd)";
        _logger.Debug($"Retrieving {url}");
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        if (_proxy != null)
        {
            handler.UseProxy = true;
            handler.Proxy = new WebProxy(_proxy);
            _logger.Debug($"Configured to use proxy {_proxy}");
        }

        using var client = new HttpClient(handler);
        var response = await client.GetFromJsonAsync<JsonElement>(url, _cancellationToken);
        return response;
    }
}