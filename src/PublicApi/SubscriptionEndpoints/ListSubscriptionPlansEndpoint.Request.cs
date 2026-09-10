using System.Threading;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansRequest : BaseRequest
{
    [JsonIgnore]
    public CancellationToken CancellationToken { get; set; }
}
