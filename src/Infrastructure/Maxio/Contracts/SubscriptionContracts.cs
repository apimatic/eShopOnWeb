using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

/// <summary>Wire model for the specification's <c>Subscription</c> schema (only the members this integration reads).</summary>
public sealed record MaxioSubscription
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    /// <summary>Specification's <c>Subscription State</c> enum.</summary>
    [JsonPropertyName("state")]
    public string? State { get; init; }

    [JsonPropertyName("reference")]
    public string? Reference { get; init; }

    [JsonPropertyName("balance_in_cents")]
    public long BalanceInCents { get; init; }

    [JsonPropertyName("total_revenue_in_cents")]
    public long TotalRevenueInCents { get; init; }

    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; init; }

    [JsonPropertyName("current_period_started_at")]
    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When capture of the next payment will be attempted.</summary>
    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; init; }

    [JsonPropertyName("trial_started_at")]
    public DateTimeOffset? TrialStartedAt { get; init; }

    [JsonPropertyName("trial_ended_at")]
    public DateTimeOffset? TrialEndedAt { get; init; }

    [JsonPropertyName("activated_at")]
    public DateTimeOffset? ActivatedAt { get; init; }

    [JsonPropertyName("canceled_at")]
    public DateTimeOffset? CanceledAt { get; init; }

    [JsonPropertyName("expires_at")]
    public DateTimeOffset? ExpiresAt { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>Specification's <c>Collection Method</c> enum.</summary>
    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; init; }

    [JsonPropertyName("currency")]
    public string? Currency { get; init; }

    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; init; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; init; }

    [JsonPropertyName("product_price_point_id")]
    public int? ProductPricePointId { get; init; }
}

/// <summary>Wire model for the specification's <c>Subscription Response</c> schema.</summary>
public sealed record SubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; init; }
}

/// <summary>
/// Wire model for the specification's <c>Create Subscription</c> schema, restricted to the members this
/// integration sends. Null members are omitted from the payload.
/// </summary>
public sealed record CreateSubscription
{
    /// <summary>The API handle of the product being subscribed to.</summary>
    [JsonPropertyName("product_handle")]
    public required string ProductHandle { get; init; }

    /// <summary>The identifier of an existing customer in the billing system.</summary>
    [JsonPropertyName("customer_id")]
    public int? CustomerId { get; init; }

    /// <summary>The reference value provided by this application for the subscription itself.</summary>
    [JsonPropertyName("reference")]
    public string? Reference { get; init; }

    /// <summary>Specification's <c>Collection Method</c> enum.</summary>
    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; init; }
}

/// <summary>Wire model for the specification's <c>Create Subscription Request</c> schema.</summary>
public sealed record CreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public required CreateSubscription Subscription { get; init; }
}

/// <summary>Values of the specification's <c>Collection Method</c> enum.</summary>
public static class CollectionMethods
{
    public const string Automatic = "automatic";

    /// <summary>Invoice-style collection on the Relationship Invoicing architecture.</summary>
    public const string Remittance = "remittance";

    public const string Prepaid = "prepaid";

    /// <summary>Invoice-style collection on the legacy Statements architecture.</summary>
    public const string Invoice = "invoice";
}
