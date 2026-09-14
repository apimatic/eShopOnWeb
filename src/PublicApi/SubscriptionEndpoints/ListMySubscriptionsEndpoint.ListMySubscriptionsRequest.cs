using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsRequest : BaseRequest
{
    [JsonIgnore]
    public string CustomerEmail { get; set; } = string.Empty;
}
