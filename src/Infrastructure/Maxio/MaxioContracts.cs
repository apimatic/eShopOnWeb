using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Wire-format contracts for the subset of the Maxio Advanced Billing REST API used by this
// integration. Field names/shapes are taken directly from the Billing API reference docs
// (customers, subscriptions, product-families).

internal class CustomerPayload
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

internal class CustomerEnvelope
{
    [JsonPropertyName("customer")]
    public CustomerPayload? Customer { get; set; }
}

internal class CustomerListItem
{
    [JsonPropertyName("customer")]
    public CustomerPayload? Customer { get; set; }
}

internal class CreateCustomerPayload
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

internal class CreateCustomerRequestEnvelope
{
    [JsonPropertyName("customer")]
    public CreateCustomerPayload Customer { get; set; } = new();

    [JsonPropertyName("uniqueness_token")]
    public string? UniquenessToken { get; set; }
}

internal class ProductPayload
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

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

    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; set; }
}

internal class ProductListItem
{
    [JsonPropertyName("product")]
    public ProductPayload? Product { get; set; }
}

internal class SubscriptionPayload
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("product")]
    public ProductPayload? Product { get; set; }

    [JsonPropertyName("customer")]
    public CustomerPayload? Customer { get; set; }
}

internal class SubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public SubscriptionPayload? Subscription { get; set; }
}

internal class SubscriptionListItem
{
    [JsonPropertyName("subscription")]
    public SubscriptionPayload? Subscription { get; set; }
}

internal class SiteEnvelope
{
    [JsonPropertyName("site")]
    public SitePayload? Site { get; set; }
}

internal class SitePayload
{
    [JsonPropertyName("relationship_invoicing_enabled")]
    public bool RelationshipInvoicingEnabled { get; set; }
}

internal class CreateSubscriptionPayload
{
    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }

    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;
}

internal class CreateSubscriptionRequestEnvelope
{
    [JsonPropertyName("subscription")]
    public CreateSubscriptionPayload Subscription { get; set; } = new();

    [JsonPropertyName("uniqueness_token")]
    public string? UniquenessToken { get; set; }
}
