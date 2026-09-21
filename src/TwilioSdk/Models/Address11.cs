using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

public record Address11
{
    [JsonPropertyName("channel")]
    public required Channel6 Channel { get; init; }

    [JsonPropertyName("address")]
    public required string Address { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("channelId")]
    public string? ChannelId { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
