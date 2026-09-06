using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;

/// <summary>
/// Wire contracts for Maxio's subscription resource. Shapes verified against
/// <c>POST /subscriptions.json</c> and <c>GET /customers/{id}/subscriptions.json</c> on a live site.
/// </summary>
public sealed class MaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}

public sealed class MaxioSubscription
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("balance_in_cents")]
    public long BalanceInCents { get; set; }

    /// <summary>Recurring price actually in force for this subscription.</summary>
    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }

    [JsonPropertyName("current_period_started_at")]
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When Maxio next assesses the subscription — i.e. the next billing date.</summary>
    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("activated_at")]
    public DateTimeOffset? ActivatedAt { get; set; }

    [JsonPropertyName("canceled_at")]
    public DateTimeOffset? CanceledAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

/// <summary>Request body for <c>POST /subscriptions.json</c>.</summary>
public sealed class CreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public CreateSubscriptionAttributes Subscription { get; set; } = new();

    /// <summary>
    /// Maxio's duplicate-prevention token. Accepted on any POST or PUT as a sibling of the resource
    /// body; a repeat within 60 minutes is rejected with <c>409 Conflict</c> and
    /// <c>DuplicatePrevention::DuplicateSubmissionError</c> rather than creating a second subscription.
    /// </summary>
    [JsonPropertyName("uniqueness_token")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UniquenessToken { get; set; }
}

public sealed class CreateSubscriptionAttributes
{
    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>Enrolls an existing customer; avoids Maxio creating a second customer record.</summary>
    [JsonPropertyName("customer_id")]
    public long CustomerId { get; set; }

    /// <summary>
    /// <c>remittance</c> invoices the subscriber instead of charging a card, which is what lets a
    /// plan that does not require a payment method activate immediately.
    /// </summary>
    [JsonPropertyName("payment_collection_method")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PaymentCollectionMethod { get; set; }
}
