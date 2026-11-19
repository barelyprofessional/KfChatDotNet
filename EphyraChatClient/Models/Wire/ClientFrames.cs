using System.Text.Json.Serialization;

namespace EphyraChatClient.Models.Wire;

// What we send to the chat host. Property order follows what the web client produces, not that it should matter.
// Be careful with nulls: "reason":null is sent on purpose while after_seq is left out entirely when there's no cursor

internal class PingFrame
{
    [JsonPropertyName("t")] public string T => "ping";
}

// {"t":"join","chat_room_uuid":"01a1f0f8-...","after_seq":1594}
internal class JoinFrame
{
    [JsonPropertyName("t")] public string T => "join";
    [JsonPropertyName("chat_room_uuid")]
    public required string ChatRoomUuid { get; set; }
    [JsonPropertyName("after_seq")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? AfterSeq { get; set; }
}

// {"t":"leave","chat_room_uuid":"01a1f0f8-..."}
internal class LeaveFrame
{
    [JsonPropertyName("t")] public string T => "leave";
    [JsonPropertyName("chat_room_uuid")]
    public required string ChatRoomUuid { get; set; }
}

// {"t":"send","chat_room_uuid":"...","body":"jewsh content","client_nonce":"01a1f195-..."}
internal class SendFrame
{
    [JsonPropertyName("t")] public string T => "send";
    [JsonPropertyName("chat_room_uuid")]
    public required string ChatRoomUuid { get; set; }
    [JsonPropertyName("body")]
    public required string Body { get; set; }
    [JsonPropertyName("client_nonce")]
    public required string ClientNonce { get; set; }
}

internal interface IOperationFrame
{
    string ChatOperationUuid { get; }
}

// {"t":"revise","chat_message_uuid":"...","body":"...","expected_message_state_seq":1648,"reason":null,
//  "notify_author":false,"chat_operation_uuid":"22f71d67-..."}
internal class ReviseFrame : IOperationFrame
{
    [JsonPropertyName("t")] public string T => "revise";
    [JsonPropertyName("chat_message_uuid")]
    public required string ChatMessageUuid { get; set; }
    [JsonPropertyName("body")]
    public required string Body { get; set; }
    [JsonPropertyName("expected_message_state_seq")]
    public required long ExpectedMessageStateSeq { get; set; }
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
    [JsonPropertyName("notify_author")]
    public bool NotifyAuthor { get; set; }
    [JsonPropertyName("chat_operation_uuid")]
    public required string ChatOperationUuid { get; set; }
}

// {"t":"tombstone","chat_message_uuid":"...","expected_message_state_seq":6,"reason":null,"notify_author":false,
//  "chat_operation_uuid":"153ceeb7-..."}
// Also used for "restore" which is the exact same shape
internal class MessageStateFrame : IOperationFrame
{
    [JsonPropertyName("t")]
    public required string T { get; set; }
    [JsonPropertyName("chat_message_uuid")]
    public required string ChatMessageUuid { get; set; }
    [JsonPropertyName("expected_message_state_seq")]
    public required long ExpectedMessageStateSeq { get; set; }
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
    [JsonPropertyName("notify_author")]
    public bool NotifyAuthor { get; set; }
    [JsonPropertyName("chat_operation_uuid")]
    public required string ChatOperationUuid { get; set; }
}

// {"t":"react","chat_message_uuid":"...","reaction_code":"reaction_7"}
// A null reaction_code takes your reaction back
internal class ReactFrame
{
    [JsonPropertyName("t")] public string T => "react";
    [JsonPropertyName("chat_message_uuid")]
    public required string ChatMessageUuid { get; set; }
    [JsonPropertyName("reaction_code")]
    public string? ReactionCode { get; set; }
}

// {"t":"query_operation","chat_operation_uuid":"..."}
internal class QueryOperationFrame
{
    [JsonPropertyName("t")] public string T => "query_operation";
    [JsonPropertyName("chat_operation_uuid")]
    public required string ChatOperationUuid { get; set; }
}
