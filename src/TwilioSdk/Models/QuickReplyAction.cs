using System.Text.Json.Serialization;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

public record QuickReplyAction
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("type")]
    public QuickReplyActionType? Type { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("id")]
    public string? Id { get; init; }
}
