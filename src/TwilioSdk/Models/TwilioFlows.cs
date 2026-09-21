using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TwilioSdk.Models;

/// <summary>
/// twilio/flows templates allow you to send multiple messages in a set order with text or select options
/// </summary>
public record TwilioFlows
{
    [JsonPropertyName("body")]
    public required string Body { get; init; }

    [JsonPropertyName("button_text")]
    public required string ButtonText { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("media_url")]
    public string? MediaUrl { get; init; }

    [JsonPropertyName("pages")]
    public required IReadOnlyList<FlowsPage> Pages { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }
}
