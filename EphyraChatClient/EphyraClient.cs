using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using EphyraChatClient.Internal;
using EphyraChatClient.Models;
using EphyraChatClient.Models.Events;
using EphyraChatClient.Models.Wire;
using NLog;
// It's a fucking lie. You must use conditional access or you WILL get NullReferenceErrors if an event is not in use
// ReSharper disable ConditionalAccessQualifierIsNonNullableAccordingToAPIContract

namespace EphyraChatClient;

/// <summary>
/// Client for Ephyra-Chat, the thing that replaced Sneedchat.
///
/// Unlike the old ChatClient this looks after itself: it reconnects with backoff, gets itself a fresh session from the
/// forum when the chat host stops accepting the cookie, pings, rejoins rooms from where it left off and keeps track of
/// the state edits and deletes need. You tell it which rooms you want, start it and listen to the events.
/// </summary>
public partial class EphyraClient : IAsyncDisposable
{
    public const string SubProtocol = "ephyra-chat.v1";
    public const string SessionCookieName = "ephyra_chat_session";
    /// <summary>Largest frame (as UTF-8 encoded JSON) the chat host will take</summary>
    public const int MaxFrameBytes = 16 * 1024;
    // Close code the web client uses when the host stops answering pings
    private const int PongTimeoutCloseCode = 4008;

    public event EventHandlers.OnConnectedEventHandler? OnConnected;
    public event EventHandlers.OnDisconnectedEventHandler? OnDisconnected;
    public event EventHandlers.OnSessionEstablishedEventHandler? OnSessionEstablished;
    public event EventHandlers.OnAdmissionFailedEventHandler? OnAdmissionFailed;
    public event EventHandlers.OnLinkStateChangedEventHandler? OnLinkStateChanged;
    public event EventHandlers.OnRoomJoinedEventHandler? OnRoomJoined;
    public event EventHandlers.OnRoomJoinFailedEventHandler? OnRoomJoinFailed;
    public event EventHandlers.OnRoomLeftEventHandler? OnRoomLeft;
    public event EventHandlers.OnPresenceEventHandler? OnPresence;
    public event EventHandlers.OnUsersJoinedEventHandler? OnUsersJoined;
    public event EventHandlers.OnUsersPartedEventHandler? OnUsersParted;
    public event EventHandlers.OnMessageEventHandler? OnMessagePosted;
    public event EventHandlers.OnMessageEventHandler? OnMessageRevised;
    public event EventHandlers.OnMessageEventHandler? OnMessageRestored;
    public event EventHandlers.OnMessageDeletedEventHandler? OnMessageDeleted;
    public event EventHandlers.OnReactionEventHandler? OnReaction;
    public event EventHandlers.OnSubtitleChangedEventHandler? OnSubtitleChanged;
    public event EventHandlers.OnEntryEventHandler? OnEntry;
    public event EventHandlers.OnErrorEventHandler? OnError;
    public event EventHandlers.OnUnknownFrameEventHandler? OnUnknownFrame;

    /// <summary>When anything at all was last received from the chat host (pongs included)</summary>
    public DateTime LastPacketReceived = DateTime.UtcNow;
    /// <summary>
    /// How long ago the event that's being handled right now came in, zero when the handlers are caught up.
    /// Events are handed out one at a time, so if this keeps growing a handler is stuck and nothing behind it is being
    /// delivered (messages included), even though the connection itself still looks perfectly healthy
    /// </summary>
    public TimeSpan EventBacklogAge
    {
        get
        {
            var since = Interlocked.Read(ref _dispatchingSince);
            return since == 0 ? TimeSpan.Zero : TimeSpan.FromTicks(Math.Max(0, DateTime.UtcNow.Ticks - since));
        }
    }
    /// <summary>Who the chat host says we are. Null until the first welcome</summary>
    public string? PrincipalUuid { get; private set; }
    public string? DisplayName { get; private set; }
    /// <summary>
    /// linked, headless or frozen. While frozen the host only accepts joins and pings, nothing can be posted
    /// </summary>
    public string LinkState { get; private set; } = "linked";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // Don't mangle emoji and friends into \u escapes, it just wastes the frame budget
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private EphyraClientConfigModel _config;
    private CookieContainer _cookies = new();
    private HttpClient? _httpClient;
    private readonly Lock _httpLock = new();

    private readonly Lock _lifecycleLock = new();
    private Task? _runTask;
    private CancellationTokenSource? _runCts;
    private CancellationTokenSource? _delayCts;
    private int _forceReconnect;
    private volatile bool _halted;
    private volatile Connection? _connection;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private readonly Lock _stateLock = new();
    private readonly Dictionary<string, RoomState> _rooms = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ChatPrincipalModel> _directory = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, PendingOperation> _operations = new(StringComparer.OrdinalIgnoreCase);
    private readonly MessageStateCache _messageStates = new();
    private readonly KeyedLock _messageLocks = new();

    private readonly Channel<(Action Handler, long QueuedTicks)> _events =
        Channel.CreateUnbounded<(Action Handler, long QueuedTicks)>(new UnboundedChannelOptions { SingleReader = true });
    // When the event that's being handled right now was queued, 0 if nothing is being handled
    private long _dispatchingSince;

    private class Connection(ClientWebSocket socket, CancellationTokenSource cts)
    {
        public readonly ClientWebSocket Socket = socket;
        public readonly CancellationTokenSource Cts = cts;
        public volatile bool Welcomed;
        public DateTime WelcomedAt;
        public long LastPongTicks;
        // Set when we're the ones who killed it, so the disconnect can be reported with the right code
        public int? LocalCloseCode;
        public string? LocalCloseDescription;
    }

    private class ConnectionEnd
    {
        public bool Connected;
        public bool Welcomed;
        public TimeSpan WelcomedFor;
        public int? CloseCode;
        public string? CloseDescription;
        public int? UpgradeStatus;
        public Exception? Exception;
    }

    private class RoomState(string uuid)
    {
        public readonly string Uuid = uuid;
        // We want to be in this room and will keep (re)joining it
        public bool Desired;
        public bool Joined;
        // A join has been sent and not answered yet
        public bool Joining;
        public bool JoinSentWithCursor;
        public int JoinFailures;
        public string? Kind;
        // Sequence of the last entry we've seen. Null means we don't know where we are and the next join is a fresh one
        public long? LastSeq;
        // Entries at or below this arrived as part of a join's replay rather than live
        public long? ReplayThrough;
        public bool ReplayIsHistory;
        public Dictionary<string, ChatPrincipalModel> Presence = new(StringComparer.OrdinalIgnoreCase);
    }

    private class PendingOperation(IOperationFrame frame)
    {
        public readonly IOperationFrame Frame = frame;
        public readonly TaskCompletionSource<OperationReply> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public volatile bool Queried;
        public volatile string? LastTransientCode;
    }

    private record OperationReply(CommandAckFrame? Ack, string? ErrorCode);

    public EphyraClient(EphyraClientConfigModel config)
    {
        _config = config;
        LoadCookies(config.Cookies);
        _ = Task.Run(DispatchEventsAsync);
    }

    #region Configuration and cookies

    /// <summary>
    /// Swap the config out. Takes effect from the next connection attempt
    /// </summary>
    public void UpdateConfig(EphyraClientConfigModel config)
    {
        _config = config;
        LoadCookies(config.Cookies);
        lock (_httpLock)
        {
            // Proxy might have changed and that can't be altered on a handler that's been used
            _httpClient?.Dispose();
            _httpClient = null;
        }
    }

    /// <summary>
    /// Add or replace cookies. Anything already in the jar which isn't in the dictionary is left alone, so the
    /// chat session survives you refreshing the forum cookies
    /// </summary>
    public void UpdateCookies(Dictionary<string, string> cookies)
    {
        LoadCookies(cookies);
    }

    /// <summary>
    /// Everything in the cookie jar, including the chat session cookie if we've been issued one
    /// </summary>
    public Dictionary<string, string> GetCookies()
    {
        var cookies = new Dictionary<string, string>();
        foreach (Cookie cookie in _cookies.GetAllCookies())
        {
            if (cookie.Expired) continue;
            cookies[cookie.Name] = cookie.Value;
        }

        return cookies;
    }

    /// <summary>
    /// Throw away the chat session cookie so the next connection has to get a new one from the forum
    /// </summary>
    public void ForgetSession()
    {
        foreach (Cookie cookie in _cookies.GetAllCookies())
        {
            if (cookie.Name == SessionCookieName) cookie.Expired = true;
        }
    }

    private void LoadCookies(Dictionary<string, string> cookies)
    {
        var domain = _config.CookieDomain ?? _config.ForumBaseUri.Host;
        foreach (var (name, value) in cookies)
        {
            try
            {
                _cookies.Add(new Cookie(name, value, "/", domain));
            }
            catch (CookieException e)
            {
                _logger.Error($"Couldn't add cookie '{name}' to the jar");
                _logger.Error(e);
            }
        }
    }

    private Uri ApiUri(string path)
    {
        var builder = new UriBuilder(_config.ChatEndpoint);
        var query = string.Empty;
        var queryStart = path.IndexOf('?');
        if (queryStart >= 0)
        {
            query = path[(queryStart + 1)..];
            path = path[..queryStart];
        }
        builder.Path = $"{builder.Path.TrimEnd('/')}/v1/{path}";
        builder.Query = query;
        return builder.Uri;
    }

    private Uri ConnectUri()
    {
        var builder = new UriBuilder(ApiUri("connect"));
        builder.Scheme = builder.Scheme == "https" ? "wss" : "ws";
        return builder.Uri;
    }

    private string? Origin()
    {
        if (_config.Origin != null) return _config.Origin.Length == 0 ? null : _config.Origin;
        return _config.ForumBaseUri.GetLeftPart(UriPartial.Authority);
    }

    private HttpClient Http()
    {
        lock (_httpLock)
        {
            if (_httpClient != null) return _httpClient;
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                CookieContainer = _cookies,
                UseCookies = true
            };
            if (_config.Proxy != null)
            {
                handler.Proxy = new WebProxy(_config.Proxy);
                handler.UseProxy = true;
            }

            _httpClient = new HttpClient(handler) { Timeout = _config.HttpTimeout };
            if (_config.UserAgent != null) _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(_config.UserAgent);
            return _httpClient;
        }
    }

    #endregion

    #region Lifecycle

    /// <summary>
    /// Start connecting. Returns straight away, wait for OnConnected / OnRoomJoined to know when it's usable.
    /// From here on the client keeps itself connected until you stop it or the host tells it to go away for good
    /// </summary>
    public Task StartAsync()
    {
        lock (_lifecycleLock)
        {
            if (_runTask is { IsCompleted: false }) return Task.CompletedTask;
            _halted = false;
            _runCts = new CancellationTokenSource();
            var ct = _runCts.Token;
            _runTask = Task.Run(() => RunAsync(ct), CancellationToken.None);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Close the connection and stay disconnected until StartAsync is called again
    /// </summary>
    public async Task StopAsync()
    {
        Task? run;
        lock (_lifecycleLock)
        {
            run = _runTask;
            try
            {
                _runCts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already stopped
            }
        }

        var connection = _connection;
        if (connection != null) await CloseConnectionAsync(connection, (int)WebSocketCloseStatus.NormalClosure, "Closing websocket");
        if (run != null)
        {
            try
            {
                await run;
            }
            catch (Exception e)
            {
                _logger.Error("Connection loop blew up while stopping");
                _logger.Error(e);
            }
        }
    }

    /// <summary>
    /// Same as StopAsync, kept so there's something synchronous to call
    /// </summary>
    public void Disconnect()
    {
        StopAsync().Wait();
    }

    public Task DisconnectAsync() => StopAsync();

    /// <summary>
    /// Drop whatever connection there is and connect again right now, skipping any backoff that's in progress.
    /// Also restarts a client which had given up or was never started
    /// </summary>
    public async Task ReconnectAsync()
    {
        bool running;
        lock (_lifecycleLock)
        {
            running = _runTask is { IsCompleted: false };
        }

        if (!running)
        {
            await StartAsync();
            return;
        }

        Interlocked.Exchange(ref _forceReconnect, 1);
        var connection = _connection;
        if (connection != null)
        {
            connection.LocalCloseDescription = "Reconnect requested";
            connection.Socket.Abort();
        }

        try
        {
            _delayCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The delay finished by itself in the meantime
        }
    }

    /// <summary>
    /// True once the socket is open and the host has welcomed us. You still need to be in a room to post to it
    /// </summary>
    public bool IsConnected()
    {
        var connection = _connection;
        return connection is { Welcomed: true } && connection.Socket.State == WebSocketState.Open;
    }

    /// <summary>
    /// True if the client gave up for good (banned, not permitted). ReconnectAsync gets it going again
    /// </summary>
    public bool IsHalted() => _halted;

    public bool IsJoined(string roomUuid)
    {
        if (!IsConnected()) return false;
        lock (_stateLock)
        {
            return _rooms.TryGetValue(roomUuid, out var room) && room.Joined;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _events.Writer.TryComplete();
        lock (_httpLock)
        {
            _httpClient?.Dispose();
            _httpClient = null;
        }
        GC.SuppressFinalize(this);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var attempts = 0;
        var admissionTried = false;
        var staleRetries = 0;
        var readmittedAfter4003 = false;
        while (!ct.IsCancellationRequested)
        {
            var end = await RunConnectionAsync(ct);
            if (end.Welcomed)
            {
                attempts = 0;
                admissionTried = false;
                // Same idea as the web client: a connection that held for a while earns its retries back
                if (end.WelcomedFor > TimeSpan.FromMinutes(2))
                {
                    staleRetries = 0;
                    readmittedAfter4003 = false;
                }
            }

            var forced = Interlocked.Exchange(ref _forceReconnect, 0) == 1;
            string? haltReason = null;
            var delay = TimeSpan.Zero;
            var considerAdmission = false;
            if (ct.IsCancellationRequested)
            {
                haltReason = "stopped";
            }
            else if (forced)
            {
                admissionTried = false;
                considerAdmission = end.UpgradeStatus is 401 or 403;
            }
            else switch (end.CloseCode)
            {
                case 4004:
                    haltReason = "banned";
                    break;
                case 4010:
                    haltReason = "not_permitted";
                    break;
                case 4009 when staleRetries > 0:
                    haltReason = "not_permitted";
                    break;
                case 4009:
                    // Web client waits it out for a good while and gives it exactly one more go
                    staleRetries = 1;
                    admissionTried = false;
                    delay = TimeSpan.FromSeconds(80 + Random.Shared.NextDouble() * 15);
                    break;
                case 4003 when readmittedAfter4003:
                    haltReason = "not_permitted";
                    break;
                default:
                    considerAdmission = true;
                    break;
            }

            Raise(() => OnDisconnected?.Invoke(this, new ChatDisconnectedModel
            {
                WasConnected = end.Connected,
                WasWelcomed = end.Welcomed,
                CloseCode = end.CloseCode,
                CloseDescription = end.CloseDescription,
                UpgradeStatusCode = end.UpgradeStatus,
                Exception = end.Exception,
                WillReconnect = haltReason == null,
                HaltReason = haltReason
            }));

            if (haltReason != null)
            {
                if (haltReason != "stopped")
                {
                    _logger.Error($"Chat host closed the connection with {end.CloseCode} ({haltReason}). Giving up until told to reconnect");
                    _halted = true;
                }
                return;
            }

            if (considerAdmission)
            {
                var sessionClose = end.CloseCode is 4001 or 4003;
                var refusedUpgrade = end.UpgradeStatus is 401 or 403;
                var needsTicket = sessionClose || refusedUpgrade;
                // Dropped before the welcome without being told why. Ask the host directly whether it's the session
                // (a refused upgrade already answered that, and if the socket never opened the host isn't there to ask)
                if (!needsTicket && end is { Connected: true, Welcomed: false } && !admissionTried)
                {
                    needsTicket = await SessionRefusedAsync(ct);
                }

                if (needsTicket && !admissionTried)
                {
                    admissionTried = true;
                    if (end.CloseCode == 4003) readmittedAfter4003 = true;
                    if (await AdmitAsync(ct))
                    {
                        // Straight back in with the new session. The latch stays set so a host which refuses the
                        // brand-new session as well sends us to the backoff instead of around in circles
                        continue;
                    }

                    admissionTried = false;
                    delay = ReconnectDelay(attempts++);
                }
                else if (!forced)
                {
                    admissionTried = false;
                    delay = ReconnectDelay(attempts++);
                }
            }

            if (delay > TimeSpan.Zero)
            {
                _logger.Info($"Reconnecting to chat in {delay.TotalSeconds:N1} seconds");
                await InterruptibleDelayAsync(delay, ct);
            }
        }
    }

    // Full jitter over a bounded exponential window: anywhere between 250ms and min(30s, 1s * 2^attempt)
    private static TimeSpan ReconnectDelay(int attempt)
    {
        var ceiling = Math.Min(30_000, 1000 * Math.Pow(2, Math.Clamp(attempt, 0, 5)));
        return TimeSpan.FromMilliseconds(250 + Random.Shared.NextDouble() * (ceiling - 250));
    }

    private async Task InterruptibleDelayAsync(TimeSpan delay, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _delayCts = cts;
        try
        {
            // ReconnectAsync may have been called between the connection ending and us getting here
            if (Volatile.Read(ref _forceReconnect) == 1) return;
            await Task.Delay(delay, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Either we're stopping or somebody wants the reconnect to happen now
        }
        finally
        {
            _delayCts = null;
        }
    }

    private async Task<ConnectionEnd> RunConnectionAsync(CancellationToken ct)
    {
        var end = new ConnectionEnd();
        var socket = new ClientWebSocket();
        var connection = new Connection(socket, CancellationTokenSource.CreateLinkedTokenSource(ct));
        try
        {
            socket.Options.AddSubProtocol(SubProtocol);
            socket.Options.Cookies = _cookies;
            socket.Options.CollectHttpResponseDetails = true;
            // The application level ping is what matters, this just keep overzealous routers from shitting all over it
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
            if (_config.Proxy != null) socket.Options.Proxy = new WebProxy(_config.Proxy);
            if (_config.UserAgent != null) socket.Options.SetRequestHeader("User-Agent", _config.UserAgent);
            var origin = Origin();
            if (origin != null) socket.Options.SetRequestHeader("Origin", origin);

            _connection = connection;
            var uri = ConnectUri();
            _logger.Debug($"Connecting to {uri}");
            using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(connection.Cts.Token))
            {
                connectCts.CancelAfter(_config.ConnectTimeout);
                await socket.ConnectAsync(uri, connectCts.Token);
            }

            end.Connected = true;
            _logger.Debug("Socket is open, waiting to be welcomed");
            await ReceiveLoopAsync(connection);
        }
        catch (Exception e)
        {
            end.Exception = e;
        }
        finally
        {
            if (!end.Connected) end.UpgradeStatus = UpgradeStatus(socket, end.Exception);
            end.CloseCode = connection.LocalCloseCode ?? (socket.CloseStatus == null ? null : (int)socket.CloseStatus);
            end.CloseDescription = connection.LocalCloseDescription ?? socket.CloseStatusDescription;
            end.Welcomed = connection.Welcomed;
            if (connection.Welcomed) end.WelcomedFor = DateTime.UtcNow - connection.WelcomedAt;
            _connection = null;
            try
            {
                await connection.Cts.CancelAsync();
            }
            catch (ObjectDisposedException)
            {
                // Nothing left to cancel
            }
            ResetSessionState();
            socket.Dispose();
            connection.Cts.Dispose();
        }

        if (end.Exception is OperationCanceledException && ct.IsCancellationRequested)
        {
            _logger.Info("Chat connection closed as the client is stopping");
        }
        else
        {
            _logger.Error($"Chat connection ended (or never opened). Connected => {end.Connected}; Welcomed => {end.Welcomed}; " +
                          $"Upgrade Status => {end.UpgradeStatus}; Close Code => {end.CloseCode}; Close Description => {end.CloseDescription}");
            if (end.Exception != null) _logger.Error(end.Exception);
        }

        return end;
    }

    private static int? UpgradeStatus(ClientWebSocket socket, Exception? exception)
    {
        try
        {
            if ((int)socket.HttpStatusCode != 0) return (int)socket.HttpStatusCode;
        }
        catch (Exception)
        {
            // Depending on how far the handshake got this isn't always readable
        }

        // "The server returned status code '401' when status code '101' was expected."
        for (var e = exception; e != null; e = e.InnerException)
        {
            var match = StatusCodeRegex().Match(e.Message);
            if (match.Success) return int.Parse(match.Groups[1].Value);
        }

        return null;
    }

    [GeneratedRegex(@"status code '(\d{3})'")]
    private static partial Regex StatusCodeRegex();

    private async Task ReceiveLoopAsync(Connection connection)
    {
        var socket = connection.Socket;
        var ct = connection.Cts.Token;
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        while (socket.State == WebSocketState.Open)
        {
            message.SetLength(0);
            ValueWebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer.AsMemory(), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.Info($"Chat host closed the socket: {(int?)socket.CloseStatus} {socket.CloseStatusDescription}");
                    try
                    {
                        using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, closeCts.Token);
                    }
                    catch (Exception e)
                    {
                        _logger.Debug(e, "Failed to complete the close handshake, not that it matters");
                    }

                    return;
                }

                message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            LastPacketReceived = DateTime.UtcNow;
            if (result.MessageType != WebSocketMessageType.Text) continue;
            try
            {
                HandleFrame(connection, new ReadOnlyMemory<byte>(message.GetBuffer(), 0, (int)message.Length));
            }
            catch (Exception e)
            {
                // One frame we can't make sense of shouldn't take the connection down with it
                _logger.Error("Failed to handle a frame from the chat host");
                _logger.Error(e);
                _logger.Error($"Frame contents: {Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length)}");
            }
        }
    }

    private async Task PingLoopAsync(Connection connection)
    {
        var ct = connection.Cts.Token;
        try
        {
            using var timer = new PeriodicTimer(_config.PingInterval);
            while (await timer.WaitForNextTickAsync(ct))
            {
                var sentAt = DateTime.UtcNow.Ticks;
                // While frozen etc. the ping still goes out. If it can't be sent at all the receive loop will notice soon enough
                if (!await SendFrameAsync(new PingFrame(), ct)) continue;
                await Task.Delay(_config.PongTimeout, ct);
                if (Interlocked.Read(ref connection.LastPongTicks) >= sentAt) continue;
                _logger.Error($"No pong from the chat host within {_config.PongTimeout.TotalSeconds:N0} seconds, dropping the connection");
                connection.LocalCloseCode = PongTimeoutCloseCode;
                connection.LocalCloseDescription = "Pong timeout";
                connection.Socket.Abort();
                return;
            }
        }
        catch (OperationCanceledException)
        {
            // Connection went away
        }
        catch (Exception e)
        {
            _logger.Error("Ping loop died");
            _logger.Error(e);
        }
    }

    private async Task CloseConnectionAsync(Connection connection, int code, string description)
    {
        connection.LocalCloseCode = code;
        connection.LocalCloseDescription = description;
        try
        {
            if (connection.Socket.State == WebSocketState.Open)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await connection.Socket.CloseOutputAsync((WebSocketCloseStatus)code, description, cts.Token);
            }
        }
        catch (Exception e)
        {
            _logger.Debug(e, "Polite close failed, aborting instead");
        }

        connection.Socket.Abort();
    }

    // Everything that's only true for the lifetime of one socket
    private void ResetSessionState()
    {
        lock (_stateLock)
        {
            foreach (var room in _rooms.Values)
            {
                room.Joined = false;
                room.Joining = false;
                room.ReplayThrough = null;
                room.Presence = new Dictionary<string, ChatPrincipalModel>(StringComparer.OrdinalIgnoreCase);
            }
        }

        foreach (var operation in _operations.Values)
        {
            operation.Completion.TrySetResult(new OperationReply(null, "disconnected"));
        }
    }

    #endregion

    #region Admission

    /// <summary>
    /// Whether the chat host itself says our session is no good (as opposed to it just being down)
    /// </summary>
    private async Task<bool> SessionRefusedAsync(CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ApiUri("sessions/current"));
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await Http().SendAsync(request, ct);
            // 404 is a chat host which predates the probe. 429 and 5xx are an outage and no reason to bother the forum
            return response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound;
        }
        catch (Exception e)
        {
            _logger.Debug(e, "Session probe failed, chat host is likely unreachable");
            return false;
        }
    }

    /// <summary>
    /// Get a ticket off the forum and swap it for a chat session cookie
    /// </summary>
    private async Task<bool> AdmitAsync(CancellationToken ct)
    {
        _logger.Info("Chat host doesn't accept our session, getting a new one");
        string ticket;
        try
        {
            if (_config.TicketProvider != null)
            {
                ticket = await _config.TicketProvider(ct);
            }
            else
            {
                var forum = new EphyraForumClient(Http(), _config.ForumBaseUri)
                {
                    TicketPath = _config.TicketPath, WhoisPath = _config.WhoisPath, CsrfField = _config.CsrfField
                };
                ticket = await forum.RequestTicketAsync(await forum.GetCsrfTokenAsync(ct: ct), ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return false;
        }
        catch (EphyraTicketException e)
        {
            _logger.Error("Forum didn't give us a ticket");
            _logger.Error(e);
            Raise(() => OnAdmissionFailed?.Invoke(this, new ChatAdmissionFailedModel
                { Failure = e.Failure, StatusCode = e.StatusCode, Exception = e }));
            return false;
        }
        catch (Exception e)
        {
            _logger.Error("Caught an exception while trying to get a ticket");
            _logger.Error(e);
            Raise(() => OnAdmissionFailed?.Invoke(this, new ChatAdmissionFailedModel
            {
                Failure = _config.TicketProvider != null ? ChatAdmissionFailure.TicketProviderFailed : ChatAdmissionFailure.ForumUnreachable,
                Exception = e
            }));
            return false;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, ApiUri("sessions"));
            request.Content = new StringContent(JsonSerializer.Serialize(new Dictionary<string, string> { ["ticket"] = ticket }),
                Encoding.UTF8, "application/json");
            var origin = Origin();
            if (origin != null) request.Headers.TryAddWithoutValidation("Origin", origin);
            using var response = await Http().SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.Error($"Chat host wouldn't take the ticket. Returned {(int)response.StatusCode}: {body}");
                Raise(() => OnAdmissionFailed?.Invoke(this, new ChatAdmissionFailedModel
                    { Failure = ChatAdmissionFailure.ChatHostRefused, StatusCode = (int)response.StatusCode }));
                return false;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception e)
        {
            _logger.Error("Caught an exception while swapping the ticket for a session");
            _logger.Error(e);
            Raise(() => OnAdmissionFailed?.Invoke(this, new ChatAdmissionFailedModel
                { Failure = ChatAdmissionFailure.ChatHostRefused, Exception = e }));
            return false;
        }

        _logger.Info("Got a new chat session");
        Raise(() => OnSessionEstablished?.Invoke(this));
        return true;
    }

    #endregion

    #region Incoming frames

    private void HandleFrame(Connection connection, ReadOnlyMemory<byte> utf8)
    {
        using var document = JsonDocument.Parse(utf8);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("t", out var typeElement) ||
            typeElement.ValueKind != JsonValueKind.String)
        {
            _logger.Info($"Received something from the chat host which isn't a frame: {Encoding.UTF8.GetString(utf8.Span)}");
            return;
        }

        var type = typeElement.GetString()!;
        if (_logger.IsTraceEnabled) _logger.Trace($"<= {Encoding.UTF8.GetString(utf8.Span)}");
        switch (type)
        {
            case "pong":
                Interlocked.Exchange(ref connection.LastPongTicks, DateTime.UtcNow.Ticks);
                return;
            case "welcome":
                HandleWelcome(connection, root.Deserialize<WelcomeFrame>(JsonOptions)!);
                return;
            case "joined":
                HandleJoined(root.Deserialize<JoinedFrame>(JsonOptions)!);
                return;
            case "left":
                HandleLeft(root.Deserialize<LeftFrame>(JsonOptions)!);
                return;
            case "resync":
                HandleResync(root.Deserialize<RoomFrame>(JsonOptions)!);
                return;
            case "presence":
                HandlePresence(root.Deserialize<PresenceFrame>(JsonOptions)!);
                return;
            case "entry":
                HandleEntry(root.Deserialize<EntryFrame>(JsonOptions)!);
                return;
            case "elided":
                // Something happened at this sequence that we're not allowed to see. Only the cursor moves
                var elided = root.Deserialize<RoomFrame>(JsonOptions)!;
                if (elided.RoomSeq != null) AdvanceRoom(elided.ChatRoomUuid, elided.RoomSeq.Value, out _);
                return;
            case "rendered":
                // Same message, fresh HTML. Its state sequence is all we keep
                var rendered = root.Deserialize<RenderedFrame>(JsonOptions)!;
                _messageStates.Update(rendered.ChatMessageUuid, ChatRowParser.Parse(rendered.Html)?.MessageStateSeq);
                return;
            case "command_ack":
                HandleCommandAck(root.Deserialize<CommandAckFrame>(JsonOptions)!);
                return;
            case "error":
                HandleError(root.Deserialize<ErrorFrame>(JsonOptions)!);
                return;
            case "link_state":
                var linkState = root.Deserialize<LinkStateFrame>(JsonOptions)!;
                if (linkState.State == null) return;
                _logger.Info($"Chat link state is now {linkState.State}");
                LinkState = linkState.State;
                Raise(() => OnLinkStateChanged?.Invoke(this, linkState.State));
                return;
            case "own_reactions":
            case "emoticons_changed":
            case "uploads":
                // Only of interest to something with a UI
                _logger.Debug($"Ignoring {type} frame");
                return;
            default:
                var json = Encoding.UTF8.GetString(utf8.Span);
                _logger.Info($"Received a frame which was not handled: {json}");
                Raise(() => OnUnknownFrame?.Invoke(this, type, json));
                return;
        }
    }

    private void HandleWelcome(Connection connection, WelcomeFrame frame)
    {
        PrincipalUuid = frame.PrincipalUuid;
        DisplayName = frame.DisplayName;
        LinkState = frame.LinkState ?? "linked";
        connection.WelcomedAt = DateTime.UtcNow;
        connection.Welcomed = true;
        _logger.Info($"Welcomed to chat as {frame.DisplayName} ({frame.PrincipalUuid}), link state is {LinkState}");
        Raise(() => OnConnected?.Invoke(this, new ChatWelcomeModel
        {
            PrincipalUuid = frame.PrincipalUuid,
            DisplayName = frame.DisplayName,
            LinkState = frame.LinkState ?? "linked",
            EmoticonRevision = frame.EmoticonRevision,
            UploadsAvailable = frame.UploadsAvailable,
            SideRoomUuids = frame.SideRooms?.Where(room => room.ChatRoomUuid != null).Select(room => room.ChatRoomUuid!).ToList() ?? []
        }));
        _ = Task.Run(() => PingLoopAsync(connection));
        _ = Task.Run(() => JoinDesiredRoomsAsync(connection));
    }

    private async Task JoinDesiredRoomsAsync(Connection connection)
    {
        List<string> rooms;
        lock (_stateLock)
        {
            rooms = _rooms.Values.Where(room => room.Desired).Select(room => room.Uuid).ToList();
        }

        try
        {
            var first = true;
            foreach (var room in rooms)
            {
                // Web client spaces its joins out like this, presumably the host rate limits them
                if (!first) await Task.Delay(TimeSpan.FromMilliseconds(600 + Random.Shared.Next(400)), connection.Cts.Token);
                first = false;
                await SendJoinAsync(room, false);
            }
        }
        catch (OperationCanceledException)
        {
            // Connection went away
        }
        catch (Exception e)
        {
            _logger.Error("Failed to join rooms after connecting");
            _logger.Error(e);
        }
    }

    private void HandleJoined(JoinedFrame frame)
    {
        bool resumed;
        lock (_stateLock)
        {
            var room = GetRoom(frame.ChatRoomUuid);
            resumed = room.Joining && room.JoinSentWithCursor;
            room.Joined = true;
            room.Joining = false;
            room.JoinFailures = 0;
            room.Kind = frame.ChatRoomKind;
            room.ReplayThrough = frame.HeadSeq;
            room.ReplayIsHistory = !resumed;
            // An empty room has nothing to replay, so this is the only chance to learn where the cursor is
            if (frame.HeadSeq == 0 && room.LastSeq == null) room.LastSeq = 0;
        }

        _logger.Info($"Joined {frame.ChatRoomUuid} ({frame.ChatRoomKind}), head sequence is {frame.HeadSeq}, resumed => {resumed}");
        if (frame.Members != null) Remember(frame.Members);
        Raise(() => OnRoomJoined?.Invoke(this, new ChatRoomJoinedModel
        {
            RoomUuid = frame.ChatRoomUuid, RoomKind = frame.ChatRoomKind, HeadSeq = frame.HeadSeq, Resumed = resumed
        }));
    }

    private void HandleLeft(LeftFrame frame)
    {
        lock (_stateLock)
        {
            var room = GetRoom(frame.ChatRoomUuid);
            room.Joined = false;
            room.Joining = false;
            room.Desired = false;
            room.Presence = new Dictionary<string, ChatPrincipalModel>(StringComparer.OrdinalIgnoreCase);
        }

        _logger.Info($"Left {frame.ChatRoomUuid}, reason: {frame.Reason}");
        Raise(() => OnRoomLeft?.Invoke(this, frame.ChatRoomUuid, frame.Reason));
    }

    // The host can't (or won't) replay from our cursor. The subscription is gone until we join again from the top
    private void HandleResync(RoomFrame frame)
    {
        bool desired;
        lock (_stateLock)
        {
            var room = GetRoom(frame.ChatRoomUuid);
            room.Joined = false;
            room.Joining = false;
            room.LastSeq = null;
            desired = room.Desired;
        }

        _logger.Info($"Chat host asked us to resync {frame.ChatRoomUuid}");
        if (desired) _ = SendJoinAsync(frame.ChatRoomUuid, true);
    }

    private void HandlePresence(PresenceFrame frame)
    {
        var principals = Remember(frame.Principals ?? []);
        List<ChatPrincipalModel> joined;
        List<ChatPrincipalModel> parted;
        lock (_stateLock)
        {
            var room = GetRoom(frame.ChatRoomUuid);
            var current = new Dictionary<string, ChatPrincipalModel>(StringComparer.OrdinalIgnoreCase);
            foreach (var principal in principals) current[principal.PrincipalUuid] = principal;
            joined = current.Values.Where(principal => !room.Presence.ContainsKey(principal.PrincipalUuid)).ToList();
            parted = room.Presence.Values.Where(principal => !current.ContainsKey(principal.PrincipalUuid)).ToList();
            room.Presence = current;
        }

        _logger.Debug($"Presence for {frame.ChatRoomUuid}: {principals.Count} present, {joined.Count} joined, {parted.Count} parted");
        Raise(() => OnPresence?.Invoke(this, frame.ChatRoomUuid, principals));
        if (joined.Count > 0) Raise(() => OnUsersJoined?.Invoke(this, frame.ChatRoomUuid, joined));
        if (parted.Count > 0) Raise(() => OnUsersParted?.Invoke(this, frame.ChatRoomUuid, parted));
    }

    private List<ChatPrincipalModel> Remember(List<PrincipalFrame> frames)
    {
        var principals = new List<ChatPrincipalModel>();
        foreach (var frame in frames)
        {
            if (frame.PrincipalUuid == null) continue;
            var principal = new ChatPrincipalModel
            {
                PrincipalUuid = frame.PrincipalUuid,
                DisplayName = frame.DisplayName ?? string.Empty,
                AvatarUrl = ToUri(frame.AvatarUrl),
                Badges = frame.Badges ?? []
            };
            _directory[principal.PrincipalUuid] = principal;
            principals.Add(principal);
        }

        return principals;
    }

    private static Uri? ToUri(string? value)
    {
        return !string.IsNullOrEmpty(value) && Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out var uri) ? uri : null;
    }

    /// <summary>
    /// Move a room's cursor on to seq. False if the entry should be thrown away, either because we've already had it or
    /// because there's a hole before it (in which case the room gets rejoined from the cursor to fill it in)
    /// </summary>
    private bool AdvanceRoom(string roomUuid, long seq, out ChatEntryOrigin origin)
    {
        origin = ChatEntryOrigin.Live;
        var rejoin = false;
        lock (_stateLock)
        {
            var room = GetRoom(roomUuid);
            if (room.LastSeq != null && seq <= room.LastSeq) return false;
            if (room.LastSeq != null && seq != room.LastSeq + 1)
            {
                if (room.Joining) return false;
                room.Joining = true;
                rejoin = true;
            }
            else
            {
                room.LastSeq = seq;
                if (room.ReplayThrough != null && seq <= room.ReplayThrough)
                {
                    origin = room.ReplayIsHistory ? ChatEntryOrigin.History : ChatEntryOrigin.Recovered;
                }
            }
        }

        if (!rejoin) return true;
        _logger.Info($"Missed something in {roomUuid} (got sequence {seq}), rejoining from the cursor to fill the gap");
        _ = SendJoinAsync(roomUuid, false);
        return false;
    }

    private void HandleEntry(EntryFrame frame)
    {
        if (!AdvanceRoom(frame.ChatRoomUuid, frame.RoomSeq, out var origin))
        {
            _logger.Debug($"Dropping entry {frame.RoomSeq} for {frame.ChatRoomUuid} as it's a duplicate or out of order");
            return;
        }

        var row = ChatRowParser.Parse(frame.Html);
        var at = frame.At == null ? (DateTimeOffset?)null : DateTimeOffset.FromUnixTimeSeconds(frame.At.Value);
        switch (frame.Kind)
        {
            case EntryKinds.MessagePosted:
            case EntryKinds.MessageRevised:
            case EntryKinds.MessageRestored:
            {
                if (frame.ChatMessageUuid == null) break;
                var stateSeq = frame.MessageStateSeq ?? row?.MessageStateSeq ?? frame.RoomSeq;
                _messageStates.Update(frame.ChatMessageUuid, stateSeq);
                var message = BuildMessage(frame, row, stateSeq, at, origin);
                switch (frame.Kind)
                {
                    case EntryKinds.MessagePosted:
                        Raise(() => OnMessagePosted?.Invoke(this, message));
                        break;
                    case EntryKinds.MessageRevised:
                        Raise(() => OnMessageRevised?.Invoke(this, message));
                        break;
                    default:
                        Raise(() => OnMessageRestored?.Invoke(this, message));
                        break;
                }

                break;
            }
            case EntryKinds.MessageTombstoned:
            case EntryKinds.MessagePurged:
            {
                if (frame.ChatMessageUuid == null) break;
                if (frame.Kind == EntryKinds.MessagePurged) _messageStates.Remove(frame.ChatMessageUuid);
                else _messageStates.Update(frame.ChatMessageUuid, frame.MessageStateSeq ?? frame.RoomSeq);
                var deletion = new ChatMessageDeletedModel
                {
                    RoomUuid = frame.ChatRoomUuid,
                    RoomSeq = frame.RoomSeq,
                    MessageUuid = frame.ChatMessageUuid,
                    ActorPrincipalUuid = frame.ActorPrincipalUuid,
                    At = at,
                    Purged = frame.Kind == EntryKinds.MessagePurged,
                    Reason = frame.Reason,
                    Origin = origin
                };
                Raise(() => OnMessageDeleted?.Invoke(this, deletion));
                break;
            }
            case EntryKinds.MessageReactionSet:
            {
                if (frame.ChatMessageUuid == null) break;
                // Reactions don't move a message's state, so the entry's own sequence is of no use here
                _messageStates.Update(frame.ChatMessageUuid, row?.MessageStateSeq);
                var reaction = new ChatReactionModel
                {
                    RoomUuid = frame.ChatRoomUuid,
                    RoomSeq = frame.RoomSeq,
                    MessageUuid = frame.ChatMessageUuid,
                    PrincipalUuid = frame.PrincipalUuid ?? frame.ActorPrincipalUuid,
                    ReactionCode = frame.ReactionCode,
                    At = at,
                    MessageAuthorPrincipalUuid = row?.AuthorPrincipalUuid,
                    Reactions = row?.Reactions ?? [],
                    Origin = origin
                };
                Raise(() => OnReaction?.Invoke(this, reaction));
                break;
            }
            case EntryKinds.RoomSubtitleSet:
                Raise(() => OnSubtitleChanged?.Invoke(this, frame.ChatRoomUuid, frame.ChatRoomSubtitle));
                break;
        }

        Raise(() => OnEntry?.Invoke(this, frame, origin));
    }

    private ChatMessageModel BuildMessage(EntryFrame frame, ChatRowModel? row, long stateSeq, DateTimeOffset? at, ChatEntryOrigin origin)
    {
        var authorUuid = row?.AuthorPrincipalUuid ?? frame.AuthorPrincipalUuid ?? frame.ActorPrincipalUuid ?? string.Empty;
        _directory.TryGetValue(authorUuid, out var known);
        var author = new ChatPrincipalModel
        {
            PrincipalUuid = authorUuid,
            DisplayName = row?.AuthorDisplayName ?? known?.DisplayName ?? string.Empty,
            AvatarUrl = ToUri(row?.AvatarUrl) ?? known?.AvatarUrl,
            // An empty badge list in the row is as likely to be "not rendered" as "has none"
            Badges = row?.Badges is { Count: > 0 } ? row.Badges : known?.Badges ?? []
        };
        // People who post but aren't in the presence list (hidden, or already gone) are still worth knowing by name
        if (authorUuid.Length > 0 && author.DisplayName.Length > 0 && known == null) _directory[authorUuid] = author;

        var revised = frame.Kind == EntryKinds.MessageRevised;
        DateTimeOffset? editedAt = revised ? at : null;
        if (editedAt == null && frame.EditedAt is { } edited)
        {
            if (edited.ValueKind == JsonValueKind.Number && edited.TryGetInt64(out var unix)) editedAt = DateTimeOffset.FromUnixTimeSeconds(unix);
            else if (edited.ValueKind == JsonValueKind.String) editedAt = ChatRowParser.Timestamp(edited.GetString());
        }
        // A restored message which had been edited before it was deleted doesn't say when, only that it was
        if (editedAt == null && row?.Revised == true && frame.Kind != EntryKinds.MessagePosted) editedAt = at;

        return new ChatMessageModel
        {
            RoomUuid = frame.ChatRoomUuid,
            RoomSeq = frame.RoomSeq,
            PostedSeq = row?.PostedSeq ?? (frame.Kind == EntryKinds.MessagePosted ? frame.RoomSeq : null),
            MessageStateSeq = stateSeq,
            MessageUuid = frame.ChatMessageUuid!,
            EntryUuid = frame.EntryUuid,
            Author = author,
            ActorPrincipalUuid = frame.ActorPrincipalUuid,
            BodyBbcode = frame.BodyBbcode ?? string.Empty,
            BodyHtml = row?.BodyHtml ?? string.Empty,
            RowHtml = frame.Html,
            Mentions = frame.Mentions?.Where(mention => mention.PrincipalUuid != null).Select(mention =>
                new ChatMentionModel { PrincipalUuid = mention.PrincipalUuid!, Present = mention.Present }).ToList() ?? [],
            Reactions = row?.Reactions ?? [],
            PostedAt = row?.PostedAt ?? at ?? DateTimeOffset.UtcNow,
            EditedAt = editedAt,
            Revision = frame.Revision ?? 1,
            ClientNonce = frame.ClientNonce,
            Imported = frame.Imported,
            Origin = origin
        };
    }

    private void HandleCommandAck(CommandAckFrame frame)
    {
        if (frame.ChatOperationUuid == null || !_operations.TryGetValue(frame.ChatOperationUuid, out var operation))
        {
            _logger.Debug($"Got an ack for operation {frame.ChatOperationUuid} which nobody is waiting on");
            return;
        }

        if (frame.Status == "applied") _messageStates.Update(frame.ChatMessageUuid, frame.MessageStateSeq);
        // Pending means the host has it but hasn't decided yet. The waiter keeps asking until it has
        if (frame.Status == "pending") return;
        operation.Completion.TrySetResult(new OperationReply(frame, null));
    }

    private void HandleError(ErrorFrame frame)
    {
        _logger.Info($"Chat host refused something: {frame.Code} (ref: {frame.Ref})");
        if (frame.Ref != null && _operations.TryGetValue(frame.Ref, out var operation))
        {
            if (frame.Code == ErrorCodes.OperationNotFound && operation.Queried)
            {
                // We asked how the operation was going and the host has never heard of it, so it never arrived
                operation.Queried = false;
                _ = SendFrameAsync(operation.Frame);
                return;
            }

            if (ErrorCodes.IsTransient(frame.Code))
            {
                // No way of knowing whether it was committed. The waiter will ask in a moment
                operation.LastTransientCode = frame.Code;
                return;
            }

            operation.Completion.TrySetResult(new OperationReply(null, frame.Code ?? "unknown"));
            return;
        }

        if (frame.Ref != null)
        {
            RoomState? joining = null;
            lock (_stateLock)
            {
                if (_rooms.TryGetValue(frame.Ref, out var room) && room.Joining)
                {
                    room.Joining = false;
                    room.JoinFailures++;
                    joining = room;
                }
            }

            if (joining != null)
            {
                var terminal = ErrorCodes.IsRoomDenial(frame.Code);
                Raise(() => OnRoomJoinFailed?.Invoke(this, new ChatRoomJoinFailedModel
                    { RoomUuid = joining.Uuid, Code = frame.Code, Terminal = terminal }));
                if (!terminal) _ = RetryJoinAsync(joining.Uuid, joining.JoinFailures);
                return;
            }
        }

        Raise(() => OnError?.Invoke(this, new ChatErrorModel
            { Code = frame.Code, Ref = frame.Ref, IsTransient = ErrorCodes.IsTransient(frame.Code) }));
    }

    private async Task RetryJoinAsync(string roomUuid, int failures)
    {
        var connection = _connection;
        if (connection == null) return;
        var baseDelay = Math.Min(30_000, 1000 * Math.Pow(2, Math.Min(failures - 1, 5)));
        var delay = Math.Min(30_000, baseDelay + Random.Shared.NextDouble() * Math.Min(5000, baseDelay / 4));
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(delay), connection.Cts.Token);
        }
        catch (OperationCanceledException)
        {
            // The reconnect will rejoin everything anyway
            return;
        }

        bool wanted;
        lock (_stateLock)
        {
            wanted = _rooms.TryGetValue(roomUuid, out var room) && room is { Desired: true, Joined: false, Joining: false };
        }

        if (wanted) await SendJoinAsync(roomUuid, false);
    }

    // Caller must hold _stateLock
    private RoomState GetRoom(string roomUuid)
    {
        if (_rooms.TryGetValue(roomUuid, out var room)) return room;
        room = new RoomState(roomUuid);
        _rooms[roomUuid] = room;
        return room;
    }

    #endregion

    #region Events

    private void Raise(Action action)
    {
        _events.Writer.TryWrite((action, DateTime.UtcNow.Ticks));
    }

    private async Task DispatchEventsAsync()
    {
        await foreach (var (handler, queuedTicks) in _events.Reader.ReadAllAsync())
        {
            Interlocked.Exchange(ref _dispatchingSince, queuedTicks);
            try
            {
                handler();
            }
            catch (Exception e)
            {
                _logger.Error("An event handler threw an exception");
                _logger.Error(e);
            }
            finally
            {
                Interlocked.Exchange(ref _dispatchingSince, 0);
            }
        }
    }

    #endregion
}
