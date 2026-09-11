using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Maxio.Models;

public class MaxioProductResponse
{
    [JsonPropertyName("product")]
    public MaxioProduct Product { get; set; } = default!;
}

public class MaxioCustomerResponse
{
    [JsonPropertyName("customer")]
    public MaxioCustomer Customer { get; set; } = default!;
}

public class MaxioSubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription Subscription { get; set; } = default!;
}
