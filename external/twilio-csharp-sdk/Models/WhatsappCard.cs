using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Twilio.Models;

/// <summary>
/// whatsapp/card is a structured template which can be used to send a series of related information. It must include a body and at least one additional field.
/// </summary>
public record WhatsappCard
{
    [JsonPropertyName("body")]
    public required string Body { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("footer")]
    public string? Footer { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("media")]
    public IReadOnlyList<string>? Media { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("header_text")]
    public string? HeaderText { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("actions")]
    public IReadOnlyList<CardAction>? Actions { get; init; }
}
