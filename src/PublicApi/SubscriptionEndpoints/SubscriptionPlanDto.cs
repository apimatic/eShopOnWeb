using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int PriceInCents { get; set; }
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public string BillingPeriod { get; set; } = string.Empty;
    public bool RequiresPaymentMethod { get; set; }
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string ProductFamilyName { get; set; } = string.Empty;

    public static SubscriptionPlanDto From(MaxioPlan plan)
    {
        return new SubscriptionPlanDto
        {
            Handle = plan.Handle,
            Name = plan.Name,
            Description = plan.Description,
            PriceInCents = plan.PriceInCents,
            Price = plan.PriceInCents / 100m,
            Interval = plan.Interval,
            IntervalUnit = plan.IntervalUnit,
            BillingPeriod = plan.Interval == 1 ? plan.IntervalUnit : $"{plan.Interval} {plan.IntervalUnit}s",
            RequiresPaymentMethod = plan.RequiresPaymentMethod,
            ProductFamilyHandle = plan.FamilyHandle,
            ProductFamilyName = plan.FamilyName
        };
    }
}
