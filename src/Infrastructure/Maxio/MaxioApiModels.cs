using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Wire models for the Maxio Advanced Billing (sandbox: chargify.com) REST API.
// Shapes verified against the live sandbox API (2026-10): product family lookup,
// product listing, customer create/lookup, subscription create/read/list.

internal class MaxioProductFamilyDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

internal class MaxioProductFamilyEnvelope
{
    [JsonPropertyName("product_family")]
    public MaxioProductFamilyDto? ProductFamily { get; set; }
}

internal class MaxioProductDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price_in_cents")]
    public long? PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int? Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; set; }

    [JsonPropertyName("archived_at")]
    public DateTime? ArchivedAt { get; set; }

    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; set; }
}

internal class MaxioProductEnvelope
{
    [JsonPropertyName("product")]
    public MaxioProductDto? Product { get; set; }
}

internal class MaxioCustomerDto
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

internal class MaxioCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCustomerDto? Customer { get; set; }
}

internal class MaxioSubscriptionDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTime? NextAssessmentAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTime? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [JsonPropertyName("product")]
    public MaxioProductDto? Product { get; set; }
}

internal class MaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscriptionDto? Subscription { get; set; }
}

internal class MaxioCreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public MaxioCreateCustomerFields Customer { get; set; } = new();
}

internal class MaxioCreateCustomerFields
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

internal class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public MaxioCreateSubscriptionFields Subscription { get; set; } = new();
}

internal class MaxioCreateSubscriptionFields
{
    [JsonPropertyName("product_id")]
    public int ProductId { get; set; }

    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    /// <summary>
    /// "invoice" enrolls the subscription without capturing a payment method
    /// (remittance/invoice billing); the seeded sandbox plans do not require a card.
    /// </summary>
    [JsonPropertyName("payment_collection_method")]
    public string PaymentCollectionMethod { get; set; } = "invoice";

    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;
}