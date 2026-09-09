using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Wire contracts for the Maxio Advanced Billing HTTP API. Field names and shapes were verified
// against the live sandbox and the official APIMatic-generated SDK models. Kept internal to the
// Infrastructure layer; the application layer only ever sees the domain models in
// ApplicationCore.Subscriptions.

internal sealed class MaxioCustomerWrapper
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioCustomer
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }
}

internal sealed class MaxioCreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public MaxioCustomerAttributes Customer { get; set; } = new();
}

internal sealed class MaxioCustomerAttributes
{
    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}

internal sealed class MaxioSubscriptionWrapper
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}

internal sealed class MaxioSubscription
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("current_period_started_at")]
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("total_revenue_in_cents")]
    public long TotalRevenueInCents { get; set; }

    [JsonPropertyName("current_billing_amount_in_cents")]
    public long CurrentBillingAmountInCents { get; set; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public MaxioSubscriptionAttributes Subscription { get; set; } = new();
}

internal sealed class MaxioSubscriptionAttributes
{
    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    [JsonPropertyName("customer_id")]
    public long? CustomerId { get; set; }

    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }
}

internal sealed class MaxioProductWrapper
{
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}

internal sealed class MaxioProduct
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price_in_cents")]
    public int PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; set; }

    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; set; }

    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; set; }

    [JsonPropertyName("product_family")]
    public MaxioProductFamily? ProductFamily { get; set; }
}

internal sealed class MaxioProductFamilyWrapper
{
    [JsonPropertyName("product_family")]
    public MaxioProductFamily? ProductFamily { get; set; }
}

internal sealed class MaxioProductFamily
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }
}

internal sealed class MaxioErrorResponse
{
    // Maxio returns validation failures as {"errors": ["msg", ...]} for the endpoints used here.
    [JsonPropertyName("errors")]
    public List<string>? Errors { get; set; }
}
