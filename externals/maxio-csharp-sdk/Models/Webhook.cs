using System;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record Webhook
{
    /// <summary>
    /// A string describing which event type produced the given webhook
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("event")]
    public string? Event { get; init; }

    /// <summary>
    /// The unique identifier for the webhook (unique across all of Chargify). This is not changed on a retry/replay of the same webhook, so it may be used to avoid duplicate action for the same event.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("id")]
    public long? Id { get; init; }

    /// <summary>
    /// Timestamp indicating when the webhook was created
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>
    /// Text describing the status code and/or error from the last failed attempt to send the Webhook. When a webhook is retried and accepted, this field will be cleared.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("last_error")]
    public string? LastError { get; init; }

    /// <summary>
    /// Timestamp indicating when the last non-acceptance occurred. If a webhook is later resent and accepted, this field will be cleared.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("last_error_at")]
    public DateTimeOffset? LastErrorAt { get; init; }

    /// <summary>
    /// Timestamp indicating when the webhook was accepted by the merchant endpoint. When a webhook is explicitly replayed by the merchant, this value will be cleared until it is accepted again.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("accepted_at")]
    public DateTimeOffset? AcceptedAt { get; init; }

    /// <summary>
    /// Timestamp indicating when the most recent attempt was made to send the webhook
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("last_sent_at")]
    public DateTimeOffset? LastSentAt { get; init; }

    /// <summary>
    /// The url that the endpoint was last sent to.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("last_sent_url")]
    public string? LastSentUrl { get; init; }

    /// <summary>
    /// “A boolean flag describing whether the webhook was accepted by the webhook endpoint for the most recent attempt. (Acceptance is defined by receiving a “200 OK” HTTP response within a reasonable timeframe, e.g., 15 seconds.)”
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("successful")]
    public bool? Successful { get; init; }

    /// <summary>
    /// The data sent within the webhook post
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("body")]
    public string? Body { get; init; }

    /// <summary>
    /// The calculated webhook signature
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("signature")]
    public string? Signature { get; init; }

    /// <summary>
    /// The calculated HMAC-SHA-256 webhook signature
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("signature_hmac_sha_256")]
    public string? SignatureHmacSha256 { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
