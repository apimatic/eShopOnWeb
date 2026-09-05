using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions;

// Wire-format DTOs mirroring the JSON envelopes defined in maxio-spec/components/schemas.
// Kept internal: PublicApi and ApplicationCore only ever see the mapped
// Microsoft.eShopWeb.ApplicationCore.Subscriptions types.

internal sealed class CustomerEnvelope
{
    [JsonPropertyName("customer")]
    public CustomerPayload? Customer { get; set; }
}

internal sealed class CustomerPayload
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }
}

internal sealed class CreateCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public CreateCustomerPayload Customer { get; set; } = new();
}

internal sealed class CreateCustomerPayload
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
    public ProductPayload? Product { get; set; }
}

internal sealed class ProductPayload
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price_in_cents")]
    public int PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; set; } = string.Empty;

    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; set; }

    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; set; }
}

internal sealed class SubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public SubscriptionPayload? Subscription { get; set; }
}

internal sealed class SubscriptionPayload
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("product_price_in_cents")]
    public int ProductPriceInCents { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("customer")]
    public CustomerPayload? Customer { get; set; }

    [JsonPropertyName("product")]
    public ProductPayload? Product { get; set; }
}

internal sealed class CreateSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public CreateSubscriptionPayload Subscription { get; set; } = new();
}

internal sealed class CreateSubscriptionPayload
{
    [JsonPropertyName("customer_id")]
    public long CustomerId { get; set; }

    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;

    // Both seeded plans have payment method not required; "remittance" (the Relationship
    // Invoicing collection method for off-session billing) lets signup succeed without a
    // card on file, matching maxio-spec's own "Basic" create-subscription example.
    [JsonPropertyName("payment_collection_method")]
    public string PaymentCollectionMethod { get; set; } = "remittance";
}
