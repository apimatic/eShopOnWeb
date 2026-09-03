using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record ContentText
{
    /// <summary>
    /// Content type discriminator.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; } = "TEXT";

    /// <summary>
    /// Message text content.
    /// </summary>
    [JsonPropertyName("text")]
    public required string Text { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
