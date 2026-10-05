using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Wire models mirroring the Maxio Billing API JSON (snake_case attribute names).
// Only the fields this integration consumes are mapped.

internal sealed class ProductFamilyWire
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string Handle { get; set; } = string.Empty;
}

internal sealed class ProductWire
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
    public long PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; set; } = string.Empty;

    [JsonPropertyName("taxable")]
    public bool Taxable { get; set; }

    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; set; }

    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; set; }

    [JsonPropertyName("product_family")]
    public ProductFamilyWire? ProductFamily { get; set; }
}

/// <summary>
/// Products are returned wrapped in a "product" member in most Maxio responses.
/// </summary>
internal sealed class ProductEnvelope
{
    [JsonPropertyName("product")]
    public ProductWire Product { get; set; } = new();
}

internal sealed class CustomerWire
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("organization")]
    public string? Organization { get; set; }
}

/// <summary>
/// Customers are returned wrapped in a "customer" member in single-item responses.
/// </summary>
internal sealed class CustomerEnvelope
{
    [JsonPropertyName("customer")]
    public CustomerWire Customer { get; set; } = new();
}

internal sealed class SubscriptionWire
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("activated_at")]
    public DateTimeOffset? ActivatedAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("product_price_in_cents")]
    public long? ProductPriceInCents { get; set; }

    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }

    [JsonPropertyName("product")]
    public ProductWire? Product { get; set; }

    [JsonPropertyName("customer")]
    public CustomerWire? Customer { get; set; }
}

/// <summary>
/// Subscriptions are returned wrapped in a "subscription" member in single-item responses.
/// </summary>
internal sealed class SubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public SubscriptionWire Subscription { get; set; } = new();
}

internal sealed class CreateCustomerBody
{
    [JsonPropertyName("customer")]
    public CreateCustomerCustomer Customer { get; set; } = new();
}

internal sealed class CreateCustomerCustomer
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

internal sealed class CreateSubscriptionBody
{
    [JsonPropertyName("subscription")]
    public CreateSubscriptionSubscription Subscription { get; set; } = new();
}

internal sealed class CreateSubscriptionSubscription
{
    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;

    [JsonPropertyName("customer_id")]
    public long CustomerId { get; set; }

    /// <summary>
    /// "remittance" (invoice-style) billing lets a subscription be created without a
    /// stored payment method; charges become invoices the merchant can collect on.
    /// </summary>
    [JsonPropertyName("payment_collection_method")]
    public string PaymentCollectionMethod { get; set; } = "remittance";

    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;

    [JsonPropertyName("uniqueness_token")]
    public string UniquenessToken { get; set; } = string.Empty;
}