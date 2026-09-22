using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TwilioSdk.Models;

/// <summary>
/// twilio/quick-reply templates let recipients tap, rather than type, to respond to the message.
/// </summary>
public record TwilioQuickReply
{
    [JsonPropertyName("body")]
    public required string Body { get; init; }

    [JsonPropertyName("actions")]
    public required IReadOnlyList<QuickReplyAction> Actions { get; init; }
}
