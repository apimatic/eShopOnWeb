using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>API representation of a subscription plan.</summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Recurring price in integer cents.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price in major currency units (e.g. dollars), for display.</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>Billing interval count paired with <see cref="IntervalUnit"/> (e.g. 1 "month").</summary>
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }

    public bool RequiresPaymentMethod { get; set; }

    public static SubscriptionPlanDto FromModel(SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        RequiresPaymentMethod = plan.RequiresPaymentMethod,
    };
}
