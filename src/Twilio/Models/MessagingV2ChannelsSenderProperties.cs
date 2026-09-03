using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

/// <summary>
/// The additional properties for the sender.
/// </summary>
public record MessagingV2ChannelsSenderProperties
{
    /// <summary>
    /// The quality rating of the sender.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("quality_rating")]
    public string? QualityRating { get; init; }

    /// <summary>
    /// The messaging limit of the sender.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("messaging_limit")]
    public string? MessagingLimit { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
