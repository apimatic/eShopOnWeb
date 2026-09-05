using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Wire-format models mirroring the Maxio Advanced Billing JSON payloads (snake_case).
// Kept internal: callers use the ApplicationCore.Maxio.* domain models instead.

internal class ProductEnvelope
{
    [JsonPropertyName("product")]
    public ProductWire? Product { get; set; }
}

internal class ProductWire
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

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
}

internal class CustomerEnvelope
{
    [JsonPropertyName("customer")]
    public CustomerWire? Customer { get; set; }
}

internal class CustomerWire
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;
}

internal class CreateCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public CreateCustomerWire Customer { get; set; } = null!;
}

internal class CreateCustomerWire
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

internal class SubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public SubscriptionWire? Subscription { get; set; }
}

internal class SubscriptionWire
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("product")]
    public ProductWire? Product { get; set; }
}

internal class CreateSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public CreateSubscriptionWire Subscription { get; set; } = null!;
}

internal class CreateSubscriptionWire
{
    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;

    // The seeded eShopOnWeb plans don't require a payment method, but the site still requires
    // an explicit non-"automatic" collection method to allow a subscription with no card on file.
    [JsonPropertyName("payment_collection_method")]
    public string PaymentCollectionMethod { get; set; } = "invoice";
}

internal class ErrorsEnvelope
{
    [JsonPropertyName("errors")]
    public System.Text.Json.JsonElement Errors { get; set; }
}
