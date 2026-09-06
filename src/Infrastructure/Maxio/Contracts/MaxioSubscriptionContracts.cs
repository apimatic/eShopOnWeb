using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

/// <summary>maxio-spec/components/schemas/Subscription-Response.yaml.</summary>
public sealed class MaxioSubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}

/// <summary>
/// maxio-spec/components/schemas/Subscription.yaml. Only the fields this integration consumes are
/// declared; unknown members are ignored on deserialization.
/// </summary>
public sealed class MaxioSubscription
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>Subscription-State.yaml, e.g. "active", "trialing", "canceled".</summary>
    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("balance_in_cents")]
    public long BalanceInCents { get; set; }

    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; set; }

    [JsonPropertyName("current_period_started_at")]
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>
    /// "Timestamp that indicates when capture of payment will be tried or retried." Usually tracks
    /// current_period_ends_at, so it is the value to show as the next billing date.
    /// </summary>
    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("trial_started_at")]
    public DateTimeOffset? TrialStartedAt { get; set; }

    [JsonPropertyName("trial_ended_at")]
    public DateTimeOffset? TrialEndedAt { get; set; }

    [JsonPropertyName("activated_at")]
    public DateTimeOffset? ActivatedAt { get; set; }

    [JsonPropertyName("expires_at")]
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [JsonPropertyName("canceled_at")]
    public DateTimeOffset? CanceledAt { get; set; }

    [JsonPropertyName("cancel_at_end_of_period")]
    public bool? CancelAtEndOfPeriod { get; set; }

    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}

/// <summary>maxio-spec/components/schemas/Create-Subscription-Request.yaml.</summary>
public sealed class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public MaxioCreateSubscription Subscription { get; set; } = new();
}

/// <summary>
/// maxio-spec/components/schemas/Create-Subscription.yaml. This integration always identifies the
/// product by handle and the customer by id, so only those members - plus the optional reference -
/// are declared. Members left null are omitted from the request body.
/// </summary>
public sealed class MaxioCreateSubscription
{
    /// <summary>"The API Handle of the product for which you are creating a subscription."</summary>
    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    /// <summary>"The ID of an existing customer within Chargify."</summary>
    [JsonPropertyName("customer_id")]
    public int? CustomerId { get; set; }

    /// <summary>"The reference value (provided by your app) for the subscription itself."</summary>
    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    /// <summary>
    /// Collection-Method.yaml - "The type of payment collection to be used in the subscription. For
    /// legacy Statements Architecture valid options are - `invoice`, `automatic`. For current
    /// Relationship Invoicing Architecture valid options are - `remittance`, `automatic`, `prepaid`."
    /// Omitted when null, in which case the site default applies.
    /// </summary>
    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }
}
