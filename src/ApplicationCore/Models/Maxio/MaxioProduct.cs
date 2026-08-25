using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Maxio;

/// <summary>
/// Product as defined by the Maxio Advanced Billing OpenAPI spec (Product schema).
/// Only the fields the integration consumes are mapped; unknown fields are ignored.
/// </summary>
public class MaxioProduct
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

    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; set; }

    [JsonPropertyName("archived_at")]
    public DateTime? ArchivedAt { get; set; }

    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; set; }
}

/// <summary>Wrapper per the spec's Product-Response schema ({ "product": { ... } }).</summary>
public class MaxioProductResponse
{
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}
