using Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool RequiresPaymentMethod { get; set; }

    public static SubscriptionPlanDto FromDomain(BillingPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        PriceInCents = plan.PriceInCents,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        RequiresPaymentMethod = plan.RequiresPaymentMethod
    };
}
