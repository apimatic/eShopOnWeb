using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

/// <summary>Mirrors the spec's Product Response envelope.</summary>
[MaxioSchema("Product-Response")]
public class MaxioProductResponse
{
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}
