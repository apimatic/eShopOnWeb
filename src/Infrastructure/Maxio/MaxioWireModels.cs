using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Internal wire-format models mirroring Maxio Advanced Billing's snake_case JSON payloads.
// These are intentionally kept private to the Infrastructure layer; MaxioClient maps them
// onto the ApplicationCore.Maxio.* DTOs that the rest of the app depends on.

internal sealed class CustomerWire
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

internal sealed class CustomerEnvelope
{
    [JsonPropertyName("customer")]
    public CustomerWire? Customer { get; set; }
}

internal sealed class CreateCustomerRequestBody
{
    [JsonPropertyName("customer")]
    public CreateCustomerAttributes Customer { get; set; } = new();
}

internal sealed class CreateCustomerAttributes
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

internal sealed class ProductWire
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

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
}

internal sealed class ProductEnvelope
{
    [JsonPropertyName("product")]
    public ProductWire? Product { get; set; }
}

internal sealed class SubscriptionWire
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("product")]
    public ProductWire? Product { get; set; }
}

internal sealed class SubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public SubscriptionWire? Subscription { get; set; }
}

internal sealed class CreateSubscriptionRequestBody
{
    [JsonPropertyName("subscription")]
    public CreateSubscriptionAttributes Subscription { get; set; } = new();
}

internal sealed class CreateSubscriptionAttributes
{
    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;

    // Explicit because the site's default collection method is "automatic" (immediate card
    // charge), which would fail signup for plans that don't require a payment method up front.
    [JsonPropertyName("payment_collection_method")]
    public string PaymentCollectionMethod { get; set; } = string.Empty;
}

internal sealed class SiteWire
{
    [JsonPropertyName("relationship_invoicing_enabled")]
    public bool RelationshipInvoicingEnabled { get; set; }
}

internal sealed class SiteEnvelope
{
    [JsonPropertyName("site")]
    public SiteWire? Site { get; set; }
}
