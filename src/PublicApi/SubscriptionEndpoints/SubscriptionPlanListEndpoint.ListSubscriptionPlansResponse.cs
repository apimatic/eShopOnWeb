using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansResponse : BaseResponse
{
    public List<PlanDto> Plans { get; set; } = new();

    /// <summary>False when the plan list could not be read to its end (page cap reached).</summary>
    public bool IsComplete { get; set; } = true;
}
