using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

/// <summary>maxio-spec/components/schemas/Product-Response.yaml.</summary>
public sealed class MaxioProductResponse
{
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}

/// <summary>
/// maxio-spec/components/schemas/Product.yaml - a product of a product family, which is what
/// eShopOnWeb surfaces as a subscription plan.
/// </summary>
public sealed class MaxioProduct
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

    /// <summary>"day" or "month" (Interval-Unit.yaml).</summary>
    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; set; }

    [JsonPropertyName("initial_charge_in_cents")]
    public long? InitialChargeInCents { get; set; }

    [JsonPropertyName("trial_price_in_cents")]
    public long? TrialPriceInCents { get; set; }

    [JsonPropertyName("trial_interval")]
    public int? TrialInterval { get; set; }

    [JsonPropertyName("trial_interval_unit")]
    public string? TrialIntervalUnit { get; set; }

    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; set; }

    /// <summary>"Boolean that controls whether a payment profile is required ... to sign up on this product."</summary>
    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; set; }

    [JsonPropertyName("taxable")]
    public bool Taxable { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [JsonPropertyName("product_family")]
    public MaxioProductFamily? ProductFamily { get; set; }

    [JsonPropertyName("product_price_point_name")]
    public string? ProductPricePointName { get; set; }

    [JsonPropertyName("product_price_point_handle")]
    public string? ProductPricePointHandle { get; set; }
}

/// <summary>maxio-spec/components/schemas/Product-Family.yaml (nested inside a product).</summary>
public sealed class MaxioProductFamily
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
