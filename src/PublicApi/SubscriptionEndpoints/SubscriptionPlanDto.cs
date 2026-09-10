using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// API representation of a subscribable plan.
/// </summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Recurring price in integer cents (as stored in Maxio).</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price expressed in major currency units.</summary>
    public decimal Price { get; set; }

    public string Currency { get; set; } = "USD";

    /// <summary>Numeric billing interval, e.g. 1.</summary>
    public int Interval { get; set; }

    /// <summary>Billing interval unit, e.g. "month".</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Display-friendly price string, e.g. "$299.00 / month".</summary>
    public string FormattedPrice { get; set; } = string.Empty;

    public static SubscriptionPlanDto FromDomain(SubscriptionPlan plan)
    {
        var price = plan.PriceInCents / 100m;
        return new SubscriptionPlanDto
        {
            Handle = plan.Handle,
            Name = plan.Name,
            Description = plan.Description,
            PriceInCents = plan.PriceInCents,
            Price = price,
            Currency = plan.CurrencyCode,
            Interval = plan.Interval,
            IntervalUnit = plan.IntervalUnit,
            FormattedPrice = SubscriptionFormatting.FormatPrice(price, plan.Interval, plan.IntervalUnit)
        };
    }
}

/// <summary>Shared price/interval formatting helpers for subscription payloads.</summary>
internal static class SubscriptionFormatting
{
    public static string FormatPrice(decimal price, int interval, string intervalUnit)
    {
        var amount = price.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
        if (string.IsNullOrWhiteSpace(intervalUnit))
        {
            return amount;
        }

        var period = interval > 1 ? $"{interval} {intervalUnit}s" : intervalUnit;
        return $"{amount} / {period}";
    }
}
