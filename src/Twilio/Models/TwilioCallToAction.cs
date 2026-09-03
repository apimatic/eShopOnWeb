using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Twilio.Models;

/// <summary>
/// twilio/call-to-action buttons let recipients tap to trigger actions such as launching a website or making a phone call.
/// </summary>
public record TwilioCallToAction
{
    [JsonPropertyName("body")]
    public required string Body { get; init; }

    [JsonPropertyName("actions")]
    public required IReadOnlyList<CallToActionAction> Actions { get; init; }
}
