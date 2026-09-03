using System.Text.Json.Serialization;

namespace Twilio.Models;

public record ListItem
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("item")]
    public required string Item { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("description")]
    public string? Description { get; init; }
}
