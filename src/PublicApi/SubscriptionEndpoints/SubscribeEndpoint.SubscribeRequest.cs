using System.Text.Json.Serialization;
using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>The durable handle of the plan to enrol in, for example "eshop-pro".</summary>
    public string PlanHandle { get; set; }

    /// <summary>
    /// The authenticated caller's identity. Set from the bearer token by the endpoint, never
    /// accepted from the request body.
    /// </summary>
    [JsonIgnore]
    public string? UserName { get; set; }

    [JsonIgnore]
    public CancellationToken CancellationToken { get; set; }
}
