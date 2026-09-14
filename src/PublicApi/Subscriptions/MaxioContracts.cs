using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioProductEnvelope
{
    [JsonPropertyName("product")]
    public MaxioProduct Product { get; set; } = new();
}

public sealed class MaxioProduct
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

    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; set; }

    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; set; }

    [JsonPropertyName("product_family")]
    public MaxioProductFamily ProductFamily { get; set; } = new();
}

public sealed class MaxioProductFamily
{
    [JsonPropertyName("handle")]
    public string Handle { get; set; } = string.Empty;
}

public sealed class MaxioCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCustomer Customer { get; set; } = new();
}

public sealed class MaxioSiteEnvelope
{
    [JsonPropertyName("site")]
    public MaxioSite Site { get; set; } = new();
}

public sealed class MaxioSite
{
    [JsonPropertyName("relationship_invoicing_enabled")]
    public bool RelationshipInvoicingEnabled { get; set; }
}

public sealed class MaxioCustomer
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}

public sealed class MaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription Subscription { get; set; } = new();
}

public sealed class MaxioSubscription
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomer Customer { get; set; } = new();

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}

public sealed record CreateMaxioCustomer(
    string FirstName,
    string LastName,
    string Email,
    string Reference);

public sealed record CreateMaxioSubscription(
    string ProductHandle,
    string CustomerReference,
    string SubscriptionReference,
    string UniquenessToken,
    string PaymentCollectionMethod);
