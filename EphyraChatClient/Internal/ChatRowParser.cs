using System.Globalization;
using System.Text.RegularExpressions;
using EphyraChatClient.Models.Events;
using HtmlAgilityPack;

namespace EphyraChatClient.Internal;

// Entries only carry UUIDs and the BBCode. The author's name, avatar, the original post time and the message's state
// sequence are only found in the row of HTML the host renders for the web client, so that gets picked apart here.
//
// <li class="chat-row" data-room-seq="1596" data-posted-seq="1596" data-message-uuid="..." data-author-uuid="..."
//     data-message-state-seq="1596" data-tombstoned="false" [data-revised] [data-mentions="uuid uuid"]>
//   <img class="chat-avatar" alt="" loading="lazy" src="https://kiwifarms.net/data/avatars/m/39/39819.avif?1793690110">
//   <span class="chat-author">Soup Sammiches</span>
//   <span class="chat-badges"><span data-badge="green"></span>...</span>
//   <time datetime="2026-11-19T02:12:27.961443146Z">2026-11-19 02:12:27 UTC</time>
//   <div class="chat-body">...</div>
//   <ul data-chat-reactions><li><span data-reaction-code="reaction_43" data-count="2"><img src="..." alt="Rat"></span></li></ul>
// </li>
internal static partial class ChatRowParser
{
    public static ChatRowModel? Parse(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var row = document.DocumentNode.SelectSingleNode("//li[@data-message-uuid]") ??
                  document.DocumentNode.SelectSingleNode("//*[@data-message-uuid]");
        if (row == null) return null;

        var model = new ChatRowModel
        {
            RowHtml = html,
            MessageUuid = Attribute(row, "data-message-uuid"),
            AuthorPrincipalUuid = Attribute(row, "data-author-uuid"),
            RoomSeq = Sequence(row, "data-room-seq"),
            PostedSeq = Sequence(row, "data-posted-seq"),
            MessageStateSeq = Sequence(row, "data-message-state-seq"),
            Tombstoned = Attribute(row, "data-tombstoned") == "true",
            Revised = row.Attributes.Contains("data-revised"),
            Restored = row.Attributes.Contains("data-restored")
        };

        var mentions = Attribute(row, "data-mentions");
        if (!string.IsNullOrWhiteSpace(mentions))
        {
            model.MentionedPrincipalUuids = mentions.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }

        // Direct children only for the header bits. A quote inside the body could otherwise be mistaken for them
        foreach (var child in row.ChildNodes)
        {
            if (child.NodeType != HtmlNodeType.Element) continue;
            if (child.HasClass("chat-avatar")) model.AvatarUrl = Attribute(child, "src");
            else if (child.HasClass("chat-author")) model.AuthorDisplayName = HtmlEntity.DeEntitize(child.InnerText).Trim();
            else if (child.HasClass("chat-body")) model.BodyHtml = child.InnerHtml;
            else if (child.HasClass("chat-badges"))
            {
                model.Badges = child.Descendants().Select(node => Attribute(node, "data-badge"))
                    .Where(badge => !string.IsNullOrEmpty(badge)).Select(badge => badge!).ToList();
            }
            else if (child.Name == "time") model.PostedAt = Timestamp(Attribute(child, "datetime"));
            else if (child.Attributes.Contains("data-chat-reactions"))
            {
                foreach (var reaction in child.Descendants().Where(node => node.Attributes.Contains("data-reaction-code")))
                {
                    var image = reaction.Descendants("img").FirstOrDefault();
                    model.Reactions.Add(new ChatReactionTallyModel
                    {
                        ReactionCode = Attribute(reaction, "data-reaction-code")!,
                        Count = int.TryParse(Attribute(reaction, "data-count"), out var count) ? count : 0,
                        Title = image == null ? null : Attribute(image, "alt"),
                        ImageUrl = image == null ? null : Attribute(image, "src")
                    });
                }
            }
        }

        // In case the markup gets rearranged and the header bits end up nested in a wrapper
        model.AuthorDisplayName ??= Text(row.SelectSingleNode(".//*[contains(concat(' ', normalize-space(@class), ' '), ' chat-author ')]"));
        model.BodyHtml ??= row.SelectSingleNode(".//*[contains(concat(' ', normalize-space(@class), ' '), ' chat-body ')]")?.InnerHtml;
        model.AvatarUrl ??= AttributeOrNull(row.SelectSingleNode(".//img[contains(concat(' ', normalize-space(@class), ' '), ' chat-avatar ')]"), "src");
        model.PostedAt ??= Timestamp(AttributeOrNull(row.SelectSingleNode(".//time[@datetime]"), "datetime"));
        return model;
    }

    private static string? Text(HtmlNode? node)
    {
        return node == null ? null : HtmlEntity.DeEntitize(node.InnerText).Trim();
    }

    private static string? AttributeOrNull(HtmlNode? node, string name)
    {
        return node == null ? null : Attribute(node, name);
    }

    private static string? Attribute(HtmlNode node, string name)
    {
        var attribute = node.Attributes[name];
        // HtmlAgilityPack hands attribute values back still encoded (&amp; in URLs, etc.)
        return attribute == null ? null : HtmlEntity.DeEntitize(attribute.Value);
    }

    private static long? Sequence(HtmlNode node, string name)
    {
        return long.TryParse(Attribute(node, name), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    internal static DateTimeOffset? Timestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        // The host sends nanosecond precision sometimes (2026-11-19T02:12:27.961443146Z) while .NET only goes to 100ns
        value = FractionRegex().Replace(value, match => match.Value[..8]);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    }

    [GeneratedRegex(@"\.\d{8,}")]
    private static partial Regex FractionRegex();
}
