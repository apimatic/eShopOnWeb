using System.Text.Json.Serialization;

namespace TwilioSdk.Models;

public record FlowsPageComponent
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("label")]
    public string? Label { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }
}
