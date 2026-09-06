using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.MaxioModels;

public class Product
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string Handle { get; set; } = string.Empty;

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; set; } = "month";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("require_payment_method")]
    public bool RequirePaymentMethod { get; set; }

    [JsonPropertyName("taxable")]
    public bool Taxable { get; set; }

    public decimal GetPriceInDollars() => PriceInCents / 100m;
}

public class ListProductsResponse
{
    [JsonPropertyName("products")]
    public List<Product> Products { get; set; } = new();
}
