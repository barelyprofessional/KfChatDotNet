using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace KfChatDotNetGui.Models;

public class ForumIdentityModel
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    [JsonPropertyName("username")]
    public string Username { get; set; }
    [JsonPropertyName("avatar_url")]
    public Uri AvatarUrl { get; set; }
    // Guessing it'll be the user ID as an int but no idea as this list is empty for me
    [JsonPropertyName("ignored_users")]
    public List<string> IgnoredUsers { get; set; }
    [JsonPropertyName("is_staff")]
    public bool IsStaff { get; set; }
}