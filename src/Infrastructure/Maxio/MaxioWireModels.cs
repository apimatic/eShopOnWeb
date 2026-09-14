using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Wire-shape DTOs for the Maxio Advanced Billing REST API (https://developers.maxio.com).
// Field names/shapes verified against the Maxio Advanced Billing API reference and the
// official ab-typescript-sdk controller/model docs before use.

internal sealed class ProductEnvelope
{
    [JsonPropertyName("product")]
    public ProductWire? Product { get; set; }
}

internal sealed class ProductWire
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string Handle { get; set; } = string.Empty;

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

internal sealed class CustomerEnvelope
{
    [JsonPropertyName("customer")]
    public CustomerWire? Customer { get; set; }
}

internal sealed class CustomerWire
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

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
    public CreateCustomerWire Customer { get; set; } = new();
}

internal sealed class CreateCustomerWire
{
    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;
}

internal sealed class SubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public SubscriptionWire? Subscription { get; set; }
}

internal sealed class SubscriptionWire
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("activated_at")]
    public DateTimeOffset? ActivatedAt { get; set; }

    [JsonPropertyName("product")]
    public ProductWire? Product { get; set; }
}

internal sealed class CreateSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public CreateSubscriptionWire Subscription { get; set; } = new();
}

internal sealed class CreateSubscriptionWire
{
    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;

    [JsonPropertyName("customer_id")]
    public long CustomerId { get; set; }

    [JsonPropertyName("payment_collection_method")]
    public string PaymentCollectionMethod { get; set; } = string.Empty;
}
