using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlansResponse : BaseResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();

    /// <summary>True when the list was capped before the provider signalled the end (partial result).</summary>
    public bool Truncated { get; set; }
}
