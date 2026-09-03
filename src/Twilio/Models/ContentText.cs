using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Models.Enums;

namespace Twilio.Models;

public record ContentText
{
    /// <summary>
    /// Content type discriminator.
    /// </summary>
    [JsonPropertyName("type")]
    public required Type11 Type { get; init; }

    /// <summary>
    /// Message text content.
    /// </summary>
    [JsonPropertyName("text")]
    public required string Text { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
