using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace KfChatDotNetGui.Models;

public class ForumIdentityModel
{
    [JsonProperty("id")]
    public int Id { get; set; }
    [JsonProperty("username")]
    public string Username { get; set; }
    [JsonProperty("avatar_url")]
    public Uri AvatarUrl { get; set; }
    // Guessing it'll be the user ID as an int but no idea as this list is empty for me
    [JsonProperty("ignored_users")]
    public List<string> IgnoredUsers { get; set; }
    [JsonProperty("is_staff")]
    public bool IsStaff { get; set; }
}