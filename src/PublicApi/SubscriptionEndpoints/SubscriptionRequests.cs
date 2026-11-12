namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest
{
    public string? PlanHandle { get; set; }
    /// <summary>Alias of <see cref="PlanHandle"/>.</summary>
    public string? Handle { get; set; }
}

public class SwitchPlanRequest
{
    public string? PlanHandle { get; set; }
    /// <summary>Alias of <see cref="PlanHandle"/>.</summary>
    public string? Handle { get; set; }
}

public class SetNextBillingDateRequest
{
    public string? Date { get; set; }
    /// <summary>Alias of <see cref="Date"/>.</summary>
    public string? NextBillingDate { get; set; }
}
