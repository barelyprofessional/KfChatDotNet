using System.Net;
using System.Text;
using System.Text.Json;
using EphyraChatClient.Internal;
using EphyraChatClient.Models.Events;
using EphyraChatClient.Models.Wire;

namespace EphyraChatClient;

// The half of the client you call into: rooms, posting, editing, deleting, reacting and the REST lookups
public partial class EphyraClient
{
    #region Rooms

    /// <summary>
    /// Join a room and keep it joined across reconnects. Fine to call before the client is connected, the join goes
    /// out as soon as it can. On a reconnect the room is rejoined from the last entry seen, so nothing gets missed or
    /// delivered twice
    /// </summary>
    public void JoinRoom(string roomUuid)
    {
        _ = JoinRoomAsync(roomUuid);
    }

    /// <returns>True if the join was sent, false if it'll be sent once connected. OnRoomJoined tells you when you're in</returns>
    public async Task<bool> JoinRoomAsync(string roomUuid)
    {
        lock (_stateLock)
        {
            var room = GetRoom(roomUuid);
            room.Desired = true;
            if (room.Joined || room.Joining) return true;
        }

        if (!IsConnected()) return false;
        return await SendJoinAsync(roomUuid, false);
    }

    public async Task LeaveRoomAsync(string roomUuid)
    {
        lock (_stateLock)
        {
            var room = GetRoom(roomUuid);
            room.Desired = false;
            room.Joined = false;
            room.Joining = false;
        }

        await SendFrameAsync(new LeaveFrame { ChatRoomUuid = roomUuid });
    }

    /// <summary>
    /// Forget where we were up to in a room. The next join gets the regular backlog instead of a replay from the cursor
    /// </summary>
    public void ResetRoomCursor(string roomUuid)
    {
        lock (_stateLock)
        {
            GetRoom(roomUuid).LastSeq = null;
        }
    }

    /// <summary>
    /// Sequence of the last entry received for a room, null if nothing has been yet
    /// </summary>
    public long? GetRoomCursor(string roomUuid)
    {
        lock (_stateLock)
        {
            return _rooms.TryGetValue(roomUuid, out var room) ? room.LastSeq : null;
        }
    }

    /// <summary>
    /// Tell the client where you were up to in a room, e.g. from a previous run. Only has an effect before the room is joined
    /// </summary>
    public void SetRoomCursor(string roomUuid, long lastSeq)
    {
        lock (_stateLock)
        {
            GetRoom(roomUuid).LastSeq = lastSeq;
        }
    }

    /// <summary>
    /// Who's in a room right now according to the last presence list the host sent
    /// </summary>
    public List<ChatPrincipalModel> GetPresence(string roomUuid)
    {
        lock (_stateLock)
        {
            return _rooms.TryGetValue(roomUuid, out var room) ? room.Presence.Values.ToList() : [];
        }
    }

    /// <summary>
    /// Look up anyone this client has come across, whether through a presence list or because they posted
    /// </summary>
    public ChatPrincipalModel? FindPrincipal(string principalUuid)
    {
        return _directory.GetValueOrDefault(principalUuid);
    }

    private async Task<bool> SendJoinAsync(string roomUuid, bool fresh)
    {
        var frame = new JoinFrame { ChatRoomUuid = roomUuid };
        lock (_stateLock)
        {
            var room = GetRoom(roomUuid);
            if (!fresh) frame.AfterSeq = room.LastSeq;
            room.Joining = true;
            room.JoinSentWithCursor = frame.AfterSeq != null;
        }

        _logger.Debug($"Joining {roomUuid}, after_seq => {frame.AfterSeq}");
        if (await SendFrameAsync(frame)) return true;
        lock (_stateLock)
        {
            GetRoom(roomUuid).Joining = false;
        }

        return false;
    }

    #endregion

    #region Sending

    /// <summary>
    /// A nonce for <see cref="SendMessageAsync"/>. Has to be a UUIDv7 as the host uses the timestamp inside it to
    /// enforce a ten-minute window within which resending the same nonce doesn't post the message twice
    /// </summary>
    public static string NewClientNonce() => Guid.CreateVersion7().ToString();

    /// <summary>
    /// Post a message to a room. Doesn't wait around for it to show up: the message comes back through
    /// OnMessagePosted carrying the same ClientNonce, or OnError fires with the nonce as its Ref if the host refuses it
    /// </summary>
    /// <param name="roomUuid">Room to post to. You have to be joined to it or you'd never see the echo</param>
    /// <param name="body">BBCode</param>
    /// <param name="clientNonce">From <see cref="NewClientNonce"/>. Sending the same nonce again is safe, the host posts it once</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>False if it couldn't be sent: not connected, not in the room, link frozen or the message is too big</returns>
    public async Task<bool> SendMessageAsync(string roomUuid, string body, string clientNonce, CancellationToken ct = default)
    {
        if (!IsJoined(roomUuid))
        {
            _logger.Debug($"Not sending to {roomUuid} as we're not in it right now");
            return false;
        }

        _logger.Debug($"Sending '{body}' to {roomUuid} with nonce {clientNonce}");
        return await SendFrameAsync(new SendFrame { ChatRoomUuid = roomUuid, Body = body, ClientNonce = clientNonce }, ct);
    }

    /// <summary>
    /// Edit a message. Waits for the host to confirm, so by the time this returns you know whether it worked.
    /// Edits to the same message are sent one after another in the order they were made
    /// </summary>
    /// <param name="messageUuid">UUID of the message to edit</param>
    /// <param name="newMessage">Replacement BBCode</param>
    /// <param name="reason">Reason to record, only relevant when editing someone else's message</param>
    /// <param name="notifyAuthor">Whether the author is told about it, only relevant when editing someone else's message</param>
    /// <param name="ct">Cancellation token</param>
    public Task<ChatCommandResultModel> EditMessageAsync(string messageUuid, string newMessage, string? reason = null,
        bool notifyAuthor = false, CancellationToken ct = default)
    {
        _logger.Debug($"Editing {messageUuid} with '{newMessage}'");
        return RunMessageOperationAsync(messageUuid, (stateSeq, operationUuid) => new ReviseFrame
        {
            ChatMessageUuid = messageUuid,
            Body = newMessage,
            ExpectedMessageStateSeq = stateSeq,
            Reason = reason,
            NotifyAuthor = notifyAuthor,
            ChatOperationUuid = operationUuid
        }, ct);
    }

    /// <summary>
    /// Delete (tombstone) a message. Waits for the host to confirm
    /// </summary>
    public Task<ChatCommandResultModel> DeleteMessageAsync(string messageUuid, string? reason = null,
        bool notifyAuthor = false, CancellationToken ct = default)
    {
        _logger.Debug($"Deleting {messageUuid}");
        return RunMessageOperationAsync(messageUuid, (stateSeq, operationUuid) => new MessageStateFrame
        {
            T = "tombstone",
            ChatMessageUuid = messageUuid,
            ExpectedMessageStateSeq = stateSeq,
            Reason = reason,
            NotifyAuthor = notifyAuthor,
            ChatOperationUuid = operationUuid
        }, ct);
    }

    /// <summary>
    /// Bring a tombstoned message back. Needs the permission to do so
    /// </summary>
    public Task<ChatCommandResultModel> RestoreMessageAsync(string messageUuid, string? reason = null,
        bool notifyAuthor = false, CancellationToken ct = default)
    {
        _logger.Debug($"Restoring {messageUuid}");
        return RunMessageOperationAsync(messageUuid, (stateSeq, operationUuid) => new MessageStateFrame
        {
            T = "restore",
            ChatMessageUuid = messageUuid,
            ExpectedMessageStateSeq = stateSeq,
            Reason = reason,
            NotifyAuthor = notifyAuthor,
            ChatOperationUuid = operationUuid
        }, ct);
    }

    /// <summary>
    /// React to a message. Not confirmed directly: success shows up as OnReaction, failure as OnError with the message
    /// UUID as its Ref (e.g. reaction_unknown)
    /// </summary>
    /// <param name="messageUuid">Message to react to</param>
    /// <param name="reactionCode">Something like reaction_7. Null takes your reaction back</param>
    /// <param name="ct">Cancellation token</param>
    public Task<bool> ReactAsync(string messageUuid, string? reactionCode, CancellationToken ct = default)
    {
        _logger.Debug($"Reacting to {messageUuid} with {reactionCode}");
        return SendFrameAsync(new ReactFrame { ChatMessageUuid = messageUuid, ReactionCode = reactionCode }, ct);
    }

    private async Task<ChatCommandResultModel> RunMessageOperationAsync(string messageUuid,
        Func<long, string, IOperationFrame> frameFactory, CancellationToken ct)
    {
        if (!IsConnected())
        {
            return new ChatCommandResultModel { Status = ChatCommandStatus.NotConnected, MessageUuid = messageUuid };
        }

        // Each operation needs the state sequence the previous one left behind, so one at a time per message
        using var _ = await _messageLocks.AcquireAsync(messageUuid, ct);
        var result = new ChatCommandResultModel { Status = ChatCommandStatus.UnknownMessage, MessageUuid = messageUuid };
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var stateSeq = _messageStates.Get(messageUuid);
            if (stateSeq == null)
            {
                // Not something we've seen go past (posted before we started, most likely) so ask the host
                try
                {
                    stateSeq = (await GetMessageAsync(messageUuid, ct))?.MessageStateSeq;
                }
                catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    _logger.Error($"Failed to look up the state of {messageUuid}");
                    _logger.Error(e);
                }

                if (stateSeq == null)
                {
                    _logger.Error($"Don't know the state of {messageUuid} so can't do anything to it");
                    return new ChatCommandResultModel { Status = ChatCommandStatus.UnknownMessage, MessageUuid = messageUuid, Code = "message_not_found" };
                }

                _messageStates.Update(messageUuid, stateSeq);
            }

            result = await ExecuteOperationAsync(frameFactory(stateSeq.Value, Guid.NewGuid().ToString()), messageUuid, ct);
            if (result.Status != ChatCommandStatus.Conflict) break;
            // Somebody else got in first (a moderator, or an edit made from a browser). Forget what we thought we knew
            // and let the next pass ask the host for the real state
            _logger.Info($"State of {messageUuid} wasn't what we expected ({stateSeq})" + (attempt == 0 ? ", retrying with fresh state" : string.Empty));
            _messageStates.Remove(messageUuid);
        }

        if (!result.Success)
        {
            _logger.Error($"Operation on {messageUuid} didn't go through. Status => {result.Status}; Code => {result.Code}");
        }

        return result;
    }

    private async Task<ChatCommandResultModel> ExecuteOperationAsync(IOperationFrame frame, string messageUuid, CancellationToken ct)
    {
        var operation = new PendingOperation(frame);
        _operations[frame.ChatOperationUuid] = operation;
        try
        {
            if (!await SendFrameAsync(frame, ct))
            {
                return new ChatCommandResultModel { Status = ChatCommandStatus.NotConnected, MessageUuid = messageUuid };
            }

            var deadline = DateTime.UtcNow + _config.CommandTimeout;
            var slice = TimeSpan.FromSeconds(3);
            while (!operation.Completion.Task.IsCompleted)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    return new ChatCommandResultModel
                    {
                        Status = ChatCommandStatus.TimedOut, MessageUuid = messageUuid, Code = operation.LastTransientCode
                    };
                }

                var finished = await Task.WhenAny(operation.Completion.Task, Task.Delay(remaining < slice ? remaining : slice, ct));
                if (finished == operation.Completion.Task) break;
                ct.ThrowIfCancellationRequested();
                // Nothing yet. The ack may have gone missing, the host may be sitting on it ("pending") or it was
                // rate limited. Asking is safe in every case, and if the host never got it the answer is
                // operation_not_found which makes HandleError send it again
                operation.Queried = true;
                await SendFrameAsync(new QueryOperationFrame { ChatOperationUuid = frame.ChatOperationUuid }, ct);
            }

            var reply = await operation.Completion.Task;
            if (reply.Ack != null)
            {
                return new ChatCommandResultModel
                {
                    Status = reply.Ack.Status switch
                    {
                        "applied" => ChatCommandStatus.Applied,
                        "conflict" => ChatCommandStatus.Conflict,
                        _ => ChatCommandStatus.Refused
                    },
                    Code = reply.Ack.Code ?? reply.Ack.Outcome,
                    MessageUuid = reply.Ack.ChatMessageUuid ?? messageUuid,
                    RoomSeq = reply.Ack.RoomSeq,
                    MessageStateSeq = reply.Ack.MessageStateSeq
                };
            }

            return new ChatCommandResultModel
            {
                Status = reply.ErrorCode switch
                {
                    ErrorCodes.MessageStateConflict => ChatCommandStatus.Conflict,
                    "disconnected" => ChatCommandStatus.NotConnected,
                    _ => ChatCommandStatus.Refused
                },
                Code = reply.ErrorCode,
                MessageUuid = messageUuid
            };
        }
        finally
        {
            _operations.TryRemove(frame.ChatOperationUuid, out _);
        }
    }

    private async Task<bool> SendFrameAsync(object frame, CancellationToken ct = default)
    {
        var connection = _connection;
        if (connection is not { Welcomed: true } || connection.Socket.State != WebSocketStateOpen)
        {
            _logger.Debug($"Can't send {frame.GetType().Name} as the chat isn't connected");
            return false;
        }

        // The host only takes these three while the link to the forum is frozen
        if (LinkState == "frozen" && frame is not (JoinFrame or QueryOperationFrame or PingFrame))
        {
            _logger.Info($"Not sending {frame.GetType().Name} as the chat is frozen");
            return false;
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(frame, frame.GetType(), JsonOptions);
        if (payload.Length > MaxFrameBytes)
        {
            _logger.Error($"Refusing to send {frame.GetType().Name} as it's {payload.Length} bytes and the limit is {MaxFrameBytes}");
            return false;
        }

        if (_logger.IsTraceEnabled) _logger.Trace($"=> {Encoding.UTF8.GetString(payload)}");
        try
        {
            // ClientWebSocket only tolerates one send at a time
            await _sendLock.WaitAsync(ct);
            try
            {
                await connection.Socket.SendAsync(payload.AsMemory(), System.Net.WebSockets.WebSocketMessageType.Text, true, ct);
            }
            finally
            {
                _sendLock.Release();
            }

            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            _logger.Error($"Failed to send {frame.GetType().Name}");
            _logger.Error(e);
            return false;
        }
    }

    private const System.Net.WebSockets.WebSocketState WebSocketStateOpen = System.Net.WebSockets.WebSocketState.Open;

    #endregion

    #region REST

    /// <summary>
    /// Fetch a message from the chat host. Mostly useful for the state sequence of something that was posted before
    /// this client was around
    /// </summary>
    /// <returns>Null if the host has no such message (or we're not allowed to see it)</returns>
    public async Task<ChatRowModel?> GetMessageAsync(string messageUuid, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiUri($"messages/{Uri.EscapeDataString(messageUuid)}"));
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await Http().SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!document.RootElement.TryGetProperty("html", out var html) || html.ValueKind != JsonValueKind.String) return null;
        var row = ChatRowParser.Parse(html.GetString());
        if (row != null) _messageStates.Update(row.MessageUuid ?? messageUuid, row.MessageStateSeq);
        return row;
    }

    /// <summary>
    /// List the rooms the host has along with their newest sequence. Handy for finding out what a room's UUID is
    /// </summary>
    /// <param name="kind">listed for the public rooms</param>
    /// <param name="ct">Cancellation token</param>
    public async Task<List<ChatRoomHeadModel>> GetRoomHeadsAsync(string kind = "listed", CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiUri($"rooms/heads?kind={Uri.EscapeDataString(kind)}"));
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await Http().SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var rooms = new List<ChatRoomHeadModel>();
        if (!document.RootElement.TryGetProperty("rooms", out var list) || list.ValueKind != JsonValueKind.Array) return rooms;
        foreach (var room in list.EnumerateArray())
        {
            if (room.ValueKind != JsonValueKind.Object || !room.TryGetProperty("chat_room_uuid", out var uuid) ||
                uuid.ValueKind != JsonValueKind.String) continue;
            rooms.Add(new ChatRoomHeadModel
            {
                RoomUuid = uuid.GetString()!,
                HeadSeq = room.TryGetProperty("head_seq", out var head) && head.ValueKind == JsonValueKind.Number && head.TryGetInt64(out var seq) ? seq : null,
                RawJson = room.GetRawText()
            });
        }

        return rooms;
    }

    /// <summary>
    /// Ask the forum who a principal is using this client's cookies. Only works if the forum cookies are in the jar and
    /// nothing is standing in front of the forum, otherwise do it through your own <see cref="EphyraForumClient"/>
    /// </summary>
    public Task<ChatWhoisModel?> WhoisAsync(string principalUuid, CancellationToken ct = default)
    {
        return new EphyraForumClient(Http(), _config.ForumBaseUri) { WhoisPath = _config.WhoisPath }.WhoisAsync(principalUuid, ct);
    }

    #endregion
}
