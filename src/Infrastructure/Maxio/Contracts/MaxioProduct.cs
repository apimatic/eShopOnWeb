using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

/// <summary>
/// Maxio OpenAPI schema <c>Product</c> (components/schemas/Product.yaml) — a subscribable plan.
/// Only the fields this integration reads are modelled.
/// </summary>
public class MaxioProduct
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>The product API handle. Stable across re-seeds, unlike <see cref="Id"/>.</summary>
    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    /// <summary>Schema <c>Interval-Unit</c>: "day" or "month".</summary>
    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; set; }

    [JsonPropertyName("trial_price_in_cents")]
    public long? TrialPriceInCents { get; set; }

    [JsonPropertyName("trial_interval")]
    public int? TrialInterval { get; set; }

    [JsonPropertyName("trial_interval_unit")]
    public string? TrialIntervalUnit { get; set; }

    /// <summary>Non-null once the plan has been archived and can no longer be subscribed to.</summary>
    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; set; }

    /// <summary>Whether a payment profile must be stored before subscribing to this plan.</summary>
    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; set; }

    [JsonPropertyName("taxable")]
    public bool Taxable { get; set; }

    [JsonPropertyName("product_family")]
    public MaxioProductFamily? ProductFamily { get; set; }

    [JsonPropertyName("product_price_point_name")]
    public string? ProductPricePointName { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Maxio OpenAPI schema <c>Product-Response</c> (components/schemas/Product-Response.yaml).</summary>
public class MaxioProductEnvelope
{
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}
