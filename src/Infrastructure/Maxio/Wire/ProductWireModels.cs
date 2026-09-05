using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Wire;

// Wire-format models mirroring maxio-spec/components/schemas/Product.yaml and Product-Response.yaml.

internal sealed class ProductWire
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

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
}

internal sealed class ProductResponseWire
{
    [JsonPropertyName("product")]
    public ProductWire? Product { get; set; }
}
