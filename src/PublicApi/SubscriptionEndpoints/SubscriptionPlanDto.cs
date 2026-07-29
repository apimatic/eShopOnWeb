using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>API projection of a subscribable plan.</summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public long PriceInCents { get; set; }
    public string Currency { get; set; } = "USD";
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Display-friendly price, e.g. "$299.00 / month".</summary>
    public string FormattedPrice { get; set; } = string.Empty;

    /// <summary>Whether a stored payment method is required to subscribe to this plan.</summary>
    public bool RequiresPaymentMethod { get; set; }

    public static SubscriptionPlanDto FromDomain(SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.Price,
        PriceInCents = plan.PriceInCents,
        Currency = plan.Currency,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        RequiresPaymentMethod = plan.RequiresPaymentMethod,
        FormattedPrice = FormatPrice(plan.Price, plan.Currency, plan.Interval, plan.IntervalUnit)
    };

    private static string FormatPrice(decimal price, string currency, int interval, string intervalUnit)
    {
        var amount = price.ToString("0.00", CultureInfo.InvariantCulture);
        var symbol = string.Equals(currency, "USD", System.StringComparison.OrdinalIgnoreCase) ? "$" : string.Empty;
        var period = interval > 1 ? $"{interval} {intervalUnit}s" : intervalUnit;
        return string.IsNullOrEmpty(period)
            ? $"{symbol}{amount} {currency}".Trim()
            : $"{symbol}{amount} {currency} / {period}".Replace("  ", " ").Trim();
    }
}
