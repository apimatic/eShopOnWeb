using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansResponse
{
    public List<SubscriptionPlan> Plans { get; set; } = new();

    /// <summary>True when the plan list was cut short by the page cap.</summary>
    public bool Truncated { get; set; }
}
