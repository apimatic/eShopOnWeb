using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record ChargifyEbb
{
    /// <summary>
    /// This timestamp determines what billing period the event will be billed in. If your request payload does not include it, Chargify will add <c>chargify.timestamp</c> to the event payload and set the value to <c>now</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("timestamp")]
    public DateTimeOffset? Timestamp { get; init; }

    /// <summary>
    /// A unique ID set by Chargify. This field is reserved. If <c>chargify.id</c> is present in the request payload, it will be overwritten.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>
    /// An ISO-8601 timestamp, set by Chargify at the time each event is recorded. This field is reserved. If <c>chargify.created_at</c> is present in the request payload, it will be overwritten.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>
    /// User-defined string scoped per-stream. Duplicate events within a stream will be silently ignored. Tokens expire after 31 days.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("uniqueness_token")]
    [MaxLength(64)]
    public string? UniquenessToken { get; init; }

    /// <summary>
    /// Id of Maxio Advanced Billing Subscription which is connected to this event.
    /// Provide <c>subscription_id</c> if you configured <c>chargify.subscription_id</c> as Subscription Identifier in your Event Stream.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("subscription_id")]
    public int? SubscriptionId { get; init; }

    /// <summary>
    /// Reference of Maxio Advanced Billing Subscription which is connected to this event.
    /// Provide <c>subscription_reference</c> if you configured <c>chargify.subscription_reference</c> as Subscription Identifier in your Event Stream.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("subscription_reference")]
    public string? SubscriptionReference { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
