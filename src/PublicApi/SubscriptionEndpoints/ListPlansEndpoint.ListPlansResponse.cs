using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListPlansResponse : BaseResponse
{
    public List<BillingPlan> Plans { get; set; } = new();
}
