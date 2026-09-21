using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

/// <summary>
/// Typing indicator request for Apple Messages for Business channel.
/// </summary>
public record AppleTypingIndicatorRequest
{
    /// <summary>
    /// The messaging channel. Must be "APPLE".
    /// </summary>
    [JsonPropertyName("channel")]
    public string Channel { get; } = "APPLE";

    /// <summary>
    /// The Apple Messages for Business identifier of the sender (business).
    /// </summary>
    [JsonPropertyName("from")]
    public required string From { get; init; }

    /// <summary>
    /// The Apple Messages for Business identifier of the recipient (customer).
    /// </summary>
    [JsonPropertyName("to")]
    public required string To { get; init; }

    /// <summary>
    /// The type of typing event. "START" indicates the agent began typing, "END" indicates the agent stopped typing. Defaults to "START".
    /// </summary>
    [JsonPropertyName("event")]
    public Event? Event { get; init; } = Event.Start;

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
