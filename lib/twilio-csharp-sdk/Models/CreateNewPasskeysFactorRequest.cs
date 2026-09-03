using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record CreateNewPasskeysFactorRequest
{
    [JsonPropertyName("friendly_name")]
    public required string FriendlyName { get; init; }

    [JsonPropertyName("identity")]
    public required string Identity { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("config")]
    public Config? Config { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
