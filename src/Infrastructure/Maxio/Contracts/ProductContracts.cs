using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

/// <summary>Wire model for the specification's <c>Product Family</c> schema.</summary>
public sealed record MaxioProductFamily
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("handle")]
    public string? Handle { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

/// <summary>Wire model for the specification's <c>Product</c> schema (only the members this integration reads).</summary>
public sealed record MaxioProduct
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("handle")]
    public string? Handle { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; init; }

    [JsonPropertyName("interval")]
    public int Interval { get; init; }

    /// <summary>Specification's <c>Interval Unit</c> enum: <c>day</c> or <c>month</c>.</summary>
    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; init; }

    [JsonPropertyName("initial_charge_in_cents")]
    public long? InitialChargeInCents { get; init; }

    [JsonPropertyName("trial_price_in_cents")]
    public long? TrialPriceInCents { get; init; }

    [JsonPropertyName("trial_interval")]
    public int? TrialInterval { get; init; }

    [JsonPropertyName("trial_interval_unit")]
    public string? TrialIntervalUnit { get; init; }

    /// <summary>Set when the product is no longer offered; archived products are not published as plans.</summary>
    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; init; }

    /// <summary>When true the billing system refuses a signup without a payment profile on file.</summary>
    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; init; }

    [JsonPropertyName("taxable")]
    public bool Taxable { get; init; }

    [JsonPropertyName("product_family")]
    public MaxioProductFamily? ProductFamily { get; init; }

    [JsonPropertyName("product_price_point_id")]
    public int? ProductPricePointId { get; init; }

    [JsonPropertyName("product_price_point_name")]
    public string? ProductPricePointName { get; init; }
}

/// <summary>Wire model for the specification's <c>Product Response</c> schema.</summary>
public sealed record ProductResponse
{
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; init; }
}
