using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Dtos;

public class MaxioCreateCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCreateCustomer Customer { get; set; } = new MaxioCreateCustomer();
}
