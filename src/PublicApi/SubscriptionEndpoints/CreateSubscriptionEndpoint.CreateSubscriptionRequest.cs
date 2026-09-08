using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    [JsonIgnore]
    public string? UserName { get; set; }

    public string PlanHandle { get; init; } = string.Empty;
}
