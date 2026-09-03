using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Twilio.Models;

/// <summary>
/// Content for a send action. Supports text, templates, and media.
/// </summary>
public record ConversationsV2SendContent
{
    /// <summary>
    /// Plain text message body.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    /// <summary>
    /// Content template ID (HX... format). When provided, the template is rendered
    /// with the variables map and sent to the recipient.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("contentId")]
    public string? ContentId { get; init; }

    /// <summary>
    /// Variables to substitute into the content template. Keys must match placeholders defined in the template.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("variables")]
    public IReadOnlyDictionary<string, string>? Variables { get; init; }

    /// <summary>
    /// URLs of media attachments to include with the message.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("mediaUrls")]
    public IReadOnlyList<string>? MediaUrls { get; init; }
}
