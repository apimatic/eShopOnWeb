using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Entities.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Recurring price in cents (integer, exact).</summary>
    public int PriceInCents { get; set; }

    /// <summary>Recurring price as a decimal amount (major currency units).</summary>
    public decimal Price { get; set; }

    /// <summary>Display-ready price, e.g. <c>$299.00 / month</c>.</summary>
    public string FormattedPrice { get; set; } = string.Empty;

    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Human-readable billing cadence, e.g. <c>month</c> or <c>3 months</c>.</summary>
    public string BillingFrequency { get; set; } = string.Empty;

    /// <summary>Whether a payment method must be captured before subscribing.</summary>
    public bool RequiresPaymentMethod { get; set; }

    public static SubscriptionPlanDto From(SubscriptionPlan plan)
    {
        var frequency = SubscriptionFormatting.Frequency(plan.Interval, plan.IntervalUnit);
        var price = plan.PriceInCents / 100m;
        return new SubscriptionPlanDto
        {
            Handle = plan.Handle,
            Name = plan.Name,
            Description = plan.Description,
            PriceInCents = plan.PriceInCents,
            Price = price,
            FormattedPrice = $"${price.ToString("0.00", CultureInfo.InvariantCulture)} / {frequency}",
            Interval = plan.Interval,
            IntervalUnit = plan.IntervalUnit,
            BillingFrequency = frequency,
            RequiresPaymentMethod = plan.RequiresPaymentMethod
        };
    }
}
