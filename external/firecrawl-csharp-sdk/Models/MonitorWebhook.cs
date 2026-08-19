using System.Collections.Generic;
using System.Text.Json.Serialization;
using FirecrawlApi.Core.Validation;
using FirecrawlApi.Core.Validation.Attributes;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

/// <summary>
/// Webhook destination for monitor page and check completion events.
/// </summary>
public record MonitorWebhook
{
    /// <summary>
    /// The URL to send monitor webhooks to.
    /// </summary>
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public required string Url { get; init; }

    /// <summary>
    /// Headers to send to the webhook URL.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("headers")]
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>
    /// Custom metadata included in webhook payloads.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("metadata")]
    public object? Metadata { get; init; }

    /// <summary>
    /// Monitor webhook events to receive. Defaults to all monitor events.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("events")]
    public IReadOnlyList<Event>? Events { get; init; }
}
