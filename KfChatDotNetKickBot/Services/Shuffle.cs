﻿using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using KfChatDotNetKickBot.Models;
using NLog;
using Websocket.Client;

namespace KfChatDotNetKickBot.Services;

public class Shuffle
{
    private Logger _logger = LogManager.GetCurrentClassLogger();
    private WebsocketClient _wsClient;
    private Uri _wsUri = new("wss://subscription-temp.shuffle.com/graphql");
    private int _reconnectTimeout = 60;
    private string? _proxy;
    public delegate void OnLatestBetUpdatedEventHandler(object sender, ShuffleLatestBetModel bet);
    public event OnLatestBetUpdatedEventHandler OnLatestBetUpdated;
    private CancellationToken _cancellationToken = CancellationToken.None;

    public Shuffle(string? proxy = null, CancellationToken? cancellationToken = null)
    {
        _proxy = proxy;
        if (cancellationToken != null) _cancellationToken = cancellationToken.Value;
        // Moved it up here as I'm concerned about the possibility of reconnections creating multiple ping tasks
        _ = PeriodicPing();
    }

    public async Task StartWsClient()
    {
        _logger.Debug("StartWsClient() called, creating client");
        await CreateWsClient();
    }

    private async Task CreateWsClient()
    {
        var factory = new Func<ClientWebSocket>(() =>
        {
            var clientWs = new ClientWebSocket();
            clientWs.Options.AddSubProtocol("graphql-transport-ws");
            clientWs.Options.SetRequestHeader("Origin", "https://shuffle.com");
            clientWs.Options.SetRequestHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:126.0) Gecko/20100101 Firefox/126.0");
            if (_proxy == null) return clientWs;
            _logger.Debug($"Using proxy address {_proxy}");
            clientWs.Options.Proxy = new WebProxy(_proxy);
            return clientWs;
        });
        
        var client = new WebsocketClient(_wsUri, factory)
        {
            ReconnectTimeout = TimeSpan.FromSeconds(_reconnectTimeout)
        };
        
        client.ReconnectionHappened.Subscribe(WsReconnection);
        client.MessageReceived.Subscribe(WsMessageReceived);
        client.DisconnectionHappened.Subscribe(WsDisconnection);

        _wsClient = client;
        
        _logger.Debug("Websocket client has been built, about to start");
        await client.Start();
        _logger.Debug("Websocket client started!");
    }
    
    public bool IsConnected()
    {
        return _wsClient is { IsRunning: true };
    }

    private void SendPing()
    {
        _logger.Debug("Sending ping to Shuffle");
        _wsClient.Send("{\"type\":\"ping\"}");
    }

    private async Task PeriodicPing()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (await timer.WaitForNextTickAsync(_cancellationToken))
        {
            if (_wsClient == null)
            {
                _logger.Debug("_wsClient doesn't exist yet, not going to try ping");
                continue;
            }
            if (!IsConnected())
            {
                _logger.Debug("Not connected not going to try send a ping actually");
                continue;
            }
            SendPing();
        }
    }
    
    private void WsDisconnection(DisconnectionInfo disconnectionInfo)
    {
        _logger.Error($"Client disconnected from the chat (or never successfully connected). Type is {disconnectionInfo.Type}");
        _logger.Error(disconnectionInfo.Exception);
    }
    
    private void WsReconnection(ReconnectionInfo reconnectionInfo)
    {
        _logger.Error($"Websocket connection dropped and reconnected. Reconnection type is {reconnectionInfo.Type}");
        _logger.Info("Sending connection_init");
        var initPayload =
            "{\"type\":\"connection_init\",\"payload\":{\"x-correlation-id\":\"pdvlnd9tej-di27abvq19-1.30.2-1i0nef1m7-g::anon\",\"authorization\":\"\"}}";
        _logger.Debug(initPayload);
        _wsClient.SendInstant(initPayload).Wait(_cancellationToken);
    }

    // Stream start JSON
    // {"type":"MESSAGE","data":{"topic":"video-playback-by-id.114122847","message":"{\"server_time\":1718631487,\"play_delay\":0,\"type\":\"stream-up\"}"}}
    // View count update (every 30 seconds)
    // {"type":"MESSAGE","data":{"topic":"video-playback-by-id.114122847","message":"{\"type\":\"viewcount\",\"server_time\":1718631500.636146,\"viewers\":62}"}}
    // {"type":"MESSAGE","data":{"topic":"video-playback-by-id.114122847","message":"{\"type\":\"viewcount\",\"server_time\":1718631530.654308,\"viewers\":162}"}}
    // {"type":"MESSAGE","data":{"topic":"video-playback-by-id.114122847","message":"{\"type\":\"viewcount\",\"server_time\":1718631560.551188,\"viewers\":179}"}}
    private void WsMessageReceived(ResponseMessage message)
    {
        if (message.Text == null)
        {
            _logger.Info("Shuffle sent a null message");
            return;
        }
        _logger.Debug($"Received event from Shuffle: {message.Text}");

        try
        {
            var packet = JsonSerializer.Deserialize<JsonElement>(message.Text);
            var packetType = packet.GetProperty("type").GetString();

            if (packetType == "connection_ack")
            {
                _logger.Debug("connection_ack packet, sending subscribe payload");
                _logger.Info("Sending subscription request");
                // we're super ghetto today
                var payload = "{\"id\":\"" + Guid.NewGuid() +
                              "\",\"type\":\"subscribe\",\"payload\":{\"variables\":{},\"extensions\":{},\"operationName\":\"LatestBetUpdated\",\"query\":\"subscription LatestBetUpdated {\\n  latestBetUpdated {\\n    ...BetActivityFields\\n    __typename\\n  }\\n}\\n\\nfragment BetActivityFields on BetActivityPayload {\\n  id\\n  username\\n  vipLevel\\n  currency\\n  amount\\n  payout\\n  multiplier\\n  gameName\\n  gameCategory\\n  gameSlug\\n  __typename\\n}\"}}";
                _logger.Debug(payload);
                _wsClient.Send(payload);
                return;
            }

            if (packetType == "pong")
            {
                _logger.Info("Shuffle pong packet");
                return;
            }

            // GAMBA
            if (packetType == "next")
            {
                _logger.Debug("Got a bet! Deserializing it");
                var bet = packet.GetProperty("payload").GetProperty("data").GetProperty("latestBetUpdated")
                    .Deserialize<ShuffleLatestBetModel>();
                if (bet == null)
                {
                    _logger.Error("Caught a null before invoking bet event");
                    throw new NullReferenceException("Caught a null before invoking bet event");
                }
                _logger.Debug("Invoking event");
                OnLatestBetUpdated?.Invoke(this, bet);
                return;
            }
            _logger.Info("Message from Shuffle was unhandled");
            _logger.Info(message.Text);
        }
        catch (Exception e)
        {
            _logger.Error("Failed to handle message from Shuffle");
            _logger.Error(e);
            _logger.Error("--- JSON Payload ---");
            _logger.Error(message.Text);
            _logger.Error("--- End of JSON Payload ---");
        }
    }
}