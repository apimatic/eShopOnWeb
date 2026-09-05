using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Wire-format contracts for the Maxio Advanced Billing REST API (https://{subdomain}.chargify.com).
// These intentionally stay private to the Infrastructure layer - ApplicationCore only ever sees
// the mapped, provider-agnostic types in Microsoft.eShopWeb.ApplicationCore.Billing.

internal sealed class CustomerEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioCustomer
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }
}

internal sealed class CreateCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public CreateCustomerBody Customer { get; set; } = new();
}

internal sealed class CreateCustomerBody
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;
}

internal sealed class ProductEnvelope
{
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}

internal sealed class MaxioProduct
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string Handle { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price_in_cents")]
    public int PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; set; } = string.Empty;

    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; set; }
}

internal sealed class SubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}

internal sealed class MaxioSubscription
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("product_price_in_cents")]
    public int ProductPriceInCents { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class CreateSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public CreateSubscriptionBody Subscription { get; set; } = new();

    [JsonPropertyName("uniqueness_token")]
    public string UniquenessToken { get; set; } = string.Empty;
}

internal sealed class CreateSubscriptionBody
{
    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>
    /// The seeded plans have no payment method requirement, but this site's default payment
    /// collection is "automatic" (immediate card charge). Requesting invoice-based ("remittance")
    /// collection lets signup succeed without a payment profile on file.
    /// </summary>
    [JsonPropertyName("payment_collection_method")]
    public string PaymentCollectionMethod { get; set; } = "invoice";
}
