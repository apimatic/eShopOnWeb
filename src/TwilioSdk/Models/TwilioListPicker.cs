using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TwilioSdk.Models;

/// <summary>
/// twilio/list-picker includes a menu of up to 10 options, which offers a simple way for users to make a selection.
/// </summary>
public record TwilioListPicker
{
    [JsonPropertyName("body")]
    public required string Body { get; init; }

    [JsonPropertyName("button")]
    public required string Button { get; init; }

    [JsonPropertyName("items")]
    public required IReadOnlyList<ListItem> Items { get; init; }
}
