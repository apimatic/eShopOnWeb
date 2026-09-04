using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal sealed class MaxioProductListResponse
{
    [JsonPropertyName("items")]
    public List<MaxioProductItem> Items { get; set; } = new();
}

internal sealed class MaxioProductItem
{
    [JsonPropertyName("product")]
    public MaxioProduct Product { get; set; } = new();
}

internal sealed class MaxioProduct
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

    [JsonPropertyName("product_family")]
    public MaxioProductFamily? ProductFamily { get; set; }
}

internal sealed class MaxioProductFamily
{
    [JsonPropertyName("handle")]
    public string? Handle { get; set; }
}

internal sealed class MaxioCustomerListResponse
{
    [JsonPropertyName("items")]
    public List<MaxioCustomerItem> Items { get; set; } = new();
}

internal sealed class MaxioCustomerItem
{
    [JsonPropertyName("customer")]
    public MaxioCustomer Customer { get; set; } = new();
}

internal sealed class MaxioCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCustomer Customer { get; set; } = new();
}

internal sealed class MaxioCustomer
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;
}

internal sealed class MaxioSiteEnvelope
{
    [JsonPropertyName("site")]
    public MaxioSite Site { get; set; } = new();
}

internal sealed class MaxioSite
{
    [JsonPropertyName("relationship_invoicing_enabled")]
    public bool RelationshipInvoicingEnabled { get; set; }
}

internal sealed class MaxioSubscriptionListResponse
{
    [JsonPropertyName("items")]
    public List<MaxioSubscriptionItem> Items { get; set; } = new();
}

internal sealed class MaxioSubscriptionItem
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription Subscription { get; set; } = new();
}

internal sealed class MaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription Subscription { get; set; } = new();
}

internal sealed class MaxioSubscription
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("product_price_in_cents")]
    public long PriceInCents { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}
