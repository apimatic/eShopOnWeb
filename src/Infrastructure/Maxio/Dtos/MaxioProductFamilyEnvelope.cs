using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Dtos;

public class MaxioProductFamilyEnvelope
{
    [JsonPropertyName("product_family")]
    public MaxioProductFamily ProductFamily { get; set; } = new MaxioProductFamily();
}
