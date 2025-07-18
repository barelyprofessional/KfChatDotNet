﻿using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using NLog;

namespace KfChatDotNetBot.Services;

public class ThreeXplPocketWatch
{
    private Logger _logger = LogManager.GetCurrentClassLogger();
    private string _3xplToken = "3A0_t3st3xplor3rpub11cb3t4efcd21748a5e";
    private string? _proxy;
    private CancellationToken _cancellationToken;
    public delegate void OnPocketWatchEventHandler(object sender, PocketWatchTransactionDbModel e);
#pragma warning disable CS0067 // Event is never used
    public event OnPocketWatchEventHandler? OnPocketWatchEvent;
#pragma warning restore CS0067 // Event is never used

    public ThreeXplPocketWatch(string? proxy = null, CancellationToken cancellationToken = default)
    {
        _logger.Info("Starting the pocket watch");
        _proxy = proxy;
        _cancellationToken = cancellationToken;
    }

    private async Task CheckAddress(PocketWatchAddressDbModel addy)
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
            $"https://api.3xpl.com/{network}/address/{address}?data=address,balances,events,mempool&from=all&token={_3xplToken}&library=currencies,rates(usd)";
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