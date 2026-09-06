using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

/// <summary>Envelope for maxio-spec components/schemas/Subscription-Response.yaml.</summary>
public class SubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}

/// <summary>maxio-spec components/schemas/Subscription.yaml (attributes consumed by this integration).</summary>
public class MaxioSubscription
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    /// <summary>See maxio-spec components/schemas/Subscription-State.yaml.</summary>
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

    /// <summary>Timestamp at which capture of the next payment will be attempted.</summary>
    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("trial_started_at")]
    public DateTimeOffset? TrialStartedAt { get; set; }

    [JsonPropertyName("trial_ended_at")]
    public DateTimeOffset? TrialEndedAt { get; set; }

    [JsonPropertyName("activated_at")]
    public DateTimeOffset? ActivatedAt { get; set; }

    [JsonPropertyName("canceled_at")]
    public DateTimeOffset? CanceledAt { get; set; }

    [JsonPropertyName("expires_at")]
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("product_price_point_id")]
    public long? ProductPricePointId { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}

/// <summary>maxio-spec components/schemas/Create-Subscription-Request.yaml.</summary>
public class CreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public CreateSubscription Subscription { get; set; } = new();
}

/// <summary>
/// maxio-spec components/schemas/Create-Subscription.yaml. Only the attributes this integration
/// sends are modelled. The plan is identified by handle because numeric ids are not stable, and
/// the customer by the id returned from the ensure-customer step.
/// </summary>
public class CreateSubscription
{
    [JsonPropertyName("product_handle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProductHandle { get; set; }

    [JsonPropertyName("product_price_point_handle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProductPricePointHandle { get; set; }

    [JsonPropertyName("customer_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? CustomerId { get; set; }

    [JsonPropertyName("customer_reference")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CustomerReference { get; set; }

    /// <summary>See maxio-spec components/schemas/Collection-Method.yaml. Null leaves the site default in place.</summary>
    [JsonPropertyName("payment_collection_method")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>The reference value (provided by this app) for the subscription itself.</summary>
    [JsonPropertyName("reference")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reference { get; set; }
}
