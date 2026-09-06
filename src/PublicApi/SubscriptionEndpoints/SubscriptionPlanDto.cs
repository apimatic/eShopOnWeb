using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A recurring plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>The stable identifier to pass back to POST /api/subscriptions.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>The recurring price in the smallest currency unit, e.g. 29900 for $299.00.</summary>
    public long PriceInCents { get; set; }

    /// <summary>The recurring price as a decimal amount, in the billing site's currency.</summary>
    public decimal Price { get; set; }

    /// <summary>The billing site's currency, e.g. "USD".</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>How often the plan renews, e.g. "1 month".</summary>
    public string BillingPeriod { get; set; } = string.Empty;

    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>True when a payment method must be captured before this plan can be subscribed to.</summary>
    public bool RequiresPaymentMethod { get; set; }

    public bool HasTrial { get; set; }
    public string? PricePointName { get; set; }

    public static SubscriptionPlanDto FromPlan(SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = plan.PriceInCents / 100m,
        Currency = plan.Currency,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        BillingPeriod = FormatPeriod(plan.Interval, plan.IntervalUnit),
        RequiresPaymentMethod = plan.RequiresPaymentMethod,
        HasTrial = plan.TrialInterval is > 0,
        PricePointName = plan.PricePointName
    };

    internal static string FormatPeriod(int interval, string? intervalUnit)
    {
        if (interval <= 0 || string.IsNullOrEmpty(intervalUnit))
        {
            return string.Empty;
        }

        return interval == 1 ? $"1 {intervalUnit}" : $"{interval} {intervalUnit}s";
    }
}
