using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;

/// <summary>
/// Wire shape of the <c>Product</c> schema in the Maxio OpenAPI specification. Only the fields
/// the subscribe capability reads are declared; property names are pinned with
/// <see cref="JsonPropertyNameAttribute"/> so they track the specification rather than a naming
/// convention.
/// </summary>
public sealed class MaxioProduct
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

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

    /// <summary>Interval-Unit enum: "day" or "month".</summary>
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

    [JsonPropertyName("expiration_interval")]
    public int? ExpirationInterval { get; set; }

    [JsonPropertyName("expiration_interval_unit")]
    public string? ExpirationIntervalUnit { get; set; }

    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; set; }

    /// <summary>
    /// Whether a payment profile has to be entered for a shopper to sign up on this product.
    /// </summary>
    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; set; }

    [JsonPropertyName("taxable")]
    public bool Taxable { get; set; }

    [JsonPropertyName("product_family")]
    public MaxioProductFamily? ProductFamily { get; set; }

    [JsonPropertyName("product_price_point_id")]
    public long? ProductPricePointId { get; set; }

    [JsonPropertyName("product_price_point_handle")]
    public string? ProductPricePointHandle { get; set; }

    [JsonPropertyName("product_price_point_name")]
    public string? ProductPricePointName { get; set; }
}
