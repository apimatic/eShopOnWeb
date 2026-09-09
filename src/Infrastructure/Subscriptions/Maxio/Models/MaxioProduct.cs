using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions.Maxio.Models;

/// <summary>Wrapper for a single product, per the spec's <c>Product-Response</c> schema.</summary>
public sealed record MaxioProductResponse
{
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; init; }
}

/// <summary>A billing-system product (a subscription plan). Fields mirror the spec's <c>Product</c> schema.</summary>
public sealed record MaxioProduct
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

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

    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; init; }

    [JsonPropertyName("archived_at")]
    public string? ArchivedAt { get; init; }

    [JsonPropertyName("product_family")]
    public MaxioProductFamily? ProductFamily { get; init; }
}

/// <summary>Nested product family, per the spec's <c>Product-Family</c> schema.</summary>
public sealed record MaxioProductFamily
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("handle")]
    public string? Handle { get; init; }
}
