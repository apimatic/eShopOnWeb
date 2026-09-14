using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

public class ListSubscriptionPlansRequest : BaseRequest
{
    public CancellationToken Ct { get; set; }
}
