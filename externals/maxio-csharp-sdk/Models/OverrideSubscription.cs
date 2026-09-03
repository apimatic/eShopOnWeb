using System;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record OverrideSubscription
{
    /// <summary>
    /// Can be used to record an external signup date. Chargify uses this field to record when a subscription first goes active (either at signup or at trial end). Only ISO8601 format is supported.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("activated_at")]
    public DateTimeOffset? ActivatedAt { get; init; }

    /// <summary>
    /// Can be used to record an external cancellation date. Chargify sets this field automatically when a subscription is canceled, whether by request or via dunning. Only ISO8601 format is supported.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("canceled_at")]
    public DateTimeOffset? CanceledAt { get; init; }

    /// <summary>
    /// Can be used to record a reason for the original cancellation.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("cancellation_message")]
    public string? CancellationMessage { get; init; }

    /// <summary>
    /// Can be used to record an external expiration date. Chargify sets this field automatically when a subscription expires (ceases billing) after a prescribed amount of time. Only ISO8601 format is supported. This field is not supported when Multi-frequency is enabled for the Site. To change the Term End of a Subscription, use the Update Subscription endpoint.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("expires_at")]
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// Can only be used when a subscription is unbilled, which happens when a future initial billing date is passed at subscription creation. The value passed must be before the current date and time. Allows you to set when the period started so mid period component allocations have the correct proration. Only ISO8601 format is supported.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("current_period_starts_at")]
    public DateTimeOffset? CurrentPeriodStartsAt { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
