using System.Text.Json.Serialization;
using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetSubscriptionPlansRequest : BaseRequest
{
    [JsonIgnore]
    public CancellationToken CancellationToken { get; set; }
}
