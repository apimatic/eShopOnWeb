using System.Collections.Generic;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;

namespace TwilioSdk.Models;

public record NumbersV1PortingWebhookConfiguration
{
    /// <summary>
    /// The URL of the webhook configuration request
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    /// <summary>
    /// The complete webhook url that will be called when a notification event for port in request or port in phone number happens
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("port_in_target_url")]
    [Format(FormatKind.Uri)]
    public string? PortInTargetUrl { get; init; }

    /// <summary>
    /// The complete webhook url that will be called when a notification event for a port out phone number happens.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("port_out_target_url")]
    [Format(FormatKind.Uri)]
    public string? PortOutTargetUrl { get; init; }

    /// <summary>
    /// A list to filter what notification events to receive for this account and its sub accounts. If it is an empty list, then it means that there are no filters for the notifications events to send in each webhook and all events will get sent.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("notifications_of")]
    public IReadOnlyList<string?>? NotificationsOf { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
