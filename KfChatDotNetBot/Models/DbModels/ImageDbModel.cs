namespace KfChatDotNetBot.Models.DbModels;

public class ImageDbModel
{
    public int Id { get; set; }
    public required string Key { get; set; }
    public required string Url { get; set; }
    public required DateTimeOffset LastSeen { get; set; }
    [Obsolete("Use TagList instead")]
    public string? Tags { get; set; }
    /// <summary>
    /// List of image tags for recalling specific images
    /// </summary>
    public required List<string> TagList { get; set; } = [];

    /// <summary>
    /// JSON object containing whatever bullshit metadata we want to attach to this image
    /// Value will be null for images that were added prior to metadata being introduced
    /// </summary>
    public required ImageMetadataModel? Metadata { get; set; }
}

public class ImageMetadataModel
{
    /// <summary>
    /// User ID (IN THE BOT, NOT KIWI FARMS USER ID) of whoever added this image
    /// </summary>
    public required int AddedByUserId { get; set; }

    /// <summary>
    /// When the image was added to the database
    /// </summary>
    public required DateTimeOffset WhenAdded { get; set; }
}