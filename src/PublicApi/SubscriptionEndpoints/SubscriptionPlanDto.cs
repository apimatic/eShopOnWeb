using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable identifier of the plan; pass this to POST api/subscriptions.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in minor units (e.g. cents).</summary>
    public int PriceInCents { get; set; }

    /// <summary>Recurring price as a decimal amount.</summary>
    public decimal Price { get; set; }

    public string? Currency { get; set; }

    /// <summary>Number of <see cref="IntervalUnit"/>s per billing period.</summary>
    public int Interval { get; set; }

    /// <summary>Billing period unit, e.g. "month".</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Human readable billing cadence, e.g. "every month".</summary>
    public string BillingPeriod { get; set; } = string.Empty;

    public bool RequiresPaymentMethod { get; set; }

    public bool HasTrial { get; set; }

    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }

    public bool Taxable { get; set; }

    public string? ProductFamilyHandle { get; set; }

    public static SubscriptionPlanDto From(SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = plan.Price,
        Currency = plan.Currency,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        BillingPeriod = FormatPeriod(plan.Interval, plan.IntervalUnit),
        RequiresPaymentMethod = plan.RequiresPaymentMethod,
        HasTrial = plan.HasTrial,
        TrialInterval = plan.TrialInterval,
        TrialIntervalUnit = plan.TrialIntervalUnit,
        Taxable = plan.Taxable,
        ProductFamilyHandle = plan.ProductFamilyHandle
    };

    internal static string FormatPeriod(int? interval, string? unit)
    {
        if (interval is not > 0 || string.IsNullOrWhiteSpace(unit))
        {
            return string.Empty;
        }

        return interval == 1 ? $"every {unit}" : $"every {interval} {unit}s";
    }
}
