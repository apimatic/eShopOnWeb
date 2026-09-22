using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TwilioSdk.Models;

/// <summary>
/// twilio/card is a structured template which can be used to send a series of related information. It must include a title and at least one additional field.
/// </summary>
public record TwilioCard
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("media")]
    public IReadOnlyList<string>? Media { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("actions")]
    public IReadOnlyList<CardAction>? Actions { get; init; }
}
