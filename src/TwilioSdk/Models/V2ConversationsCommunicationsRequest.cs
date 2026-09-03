using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Models.AnyOf;

namespace TwilioSdk.Models;

public record V2ConversationsCommunicationsRequest
{
    [JsonPropertyName("author")]
    public required Author Author { get; init; }

    /// <summary>
    /// The content of the Communication.
    /// </summary>
    [JsonPropertyName("content")]
    public required Content2 Content { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("channelId")]
    public string? ChannelId { get; init; }

    [JsonPropertyName("recipients")]
    [MinLength(1)]
    public required IReadOnlyList<Recipient2> Recipients { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
