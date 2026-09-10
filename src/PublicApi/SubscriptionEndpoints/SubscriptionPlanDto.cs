using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>API representation of a subscription plan.</summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>The recurring price in integer cents.</summary>
    public long PriceInCents { get; set; }

    /// <summary>The recurring price as a decimal amount.</summary>
    public decimal Price { get; set; }

    /// <summary>A human-friendly price/frequency label, e.g. "299.00 per month".</summary>
    public string FormattedPrice { get; set; } = string.Empty;

    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Whether a stored payment method is required to subscribe to this plan.</summary>
    public bool RequiresPaymentMethod { get; set; }

    public static SubscriptionPlanDto FromDomain(SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = plan.Price,
        FormattedPrice = FormatPrice(plan.Price, plan.Interval, plan.IntervalUnit),
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        RequiresPaymentMethod = plan.RequiresPaymentMethod,
    };

    private static string FormatPrice(decimal price, int interval, string intervalUnit)
    {
        var amount = price.ToString("0.00", CultureInfo.InvariantCulture);
        var frequency = interval <= 1 ? intervalUnit : $"{interval} {intervalUnit}s";
        return $"{amount} per {frequency}";
    }
}
