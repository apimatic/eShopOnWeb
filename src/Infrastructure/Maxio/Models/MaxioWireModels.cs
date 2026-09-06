using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

// Wire shapes for the Maxio Advanced Billing API. Deliberately anaemic and permissive: every
// field the API documents as nullable is nullable here, and nothing outside this namespace sees
// these types - MaxioApiClient maps them onto the ApplicationCore models.

internal sealed class ProductEnvelope
{
    [JsonPropertyName("product")]
    public Product? Product { get; set; }
}

internal sealed class Product
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; set; }

    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; set; }

    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; set; }

    [JsonPropertyName("product_price_point_handle")]
    public string? ProductPricePointHandle { get; set; }

    [JsonPropertyName("product_family")]
    public ProductFamily? ProductFamily { get; set; }
}

internal sealed class ProductFamily
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

internal sealed class CustomerEnvelope
{
    [JsonPropertyName("customer")]
    public Customer? Customer { get; set; }
}

internal sealed class Customer
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

internal sealed class CreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public CustomerAttributes Customer { get; set; } = new();
}

internal sealed class CustomerAttributes
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

internal sealed class SubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public Subscription? Subscription { get; set; }
}

internal sealed class Subscription
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; set; }

    [JsonPropertyName("current_period_started_at")]
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("activated_at")]
    public DateTimeOffset? ActivatedAt { get; set; }

    [JsonPropertyName("canceled_at")]
    public DateTimeOffset? CanceledAt { get; set; }

    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }

    [JsonPropertyName("customer")]
    public Customer? Customer { get; set; }

    // Null on sites using the catalog-free subscription model, where a subscription is built
    // entirely from components instead of a product.
    [JsonPropertyName("product")]
    public Product? Product { get; set; }
}

internal sealed class CreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public SubscriptionAttributes Subscription { get; set; } = new();

    /// <summary>Maxio rejects a repeat of this token within its dedupe window with 409 Conflict.</summary>
    [JsonPropertyName("uniqueness_token")]
    public string? UniquenessToken { get; set; }
}

internal sealed class SubscriptionAttributes
{
    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    [JsonPropertyName("product_price_point_handle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProductPricePointHandle { get; set; }

    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    [JsonPropertyName("reference")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reference { get; set; }

    [JsonPropertyName("payment_collection_method")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PaymentCollectionMethod { get; set; }
}

/// <summary>
/// Maxio reports failures as an "errors" member that is sometimes an array of strings and
/// sometimes an object keyed by field, so it is read loosely and flattened by the client.
/// </summary>
internal sealed class ErrorEnvelope
{
    [JsonPropertyName("errors")]
    public System.Text.Json.JsonElement Errors { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
