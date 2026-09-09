using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscribable plan offered by the billing system, in provider-agnostic terms.
/// </summary>
public class SubscriptionPlan
{
    public SubscriptionPlan(
        string handle,
        string name,
        string? description,
        int priceInCents,
        string currency,
        int interval,
        string intervalUnit)
    {
        Handle = handle;
        Name = name;
        Description = description;
        PriceInCents = priceInCents;
        Currency = currency;
        Interval = interval;
        IntervalUnit = intervalUnit;
    }

    /// <summary>Stable handle used to subscribe to this plan (e.g. "eshop-pro").</summary>
    public string Handle { get; }

    public string Name { get; }

    public string? Description { get; }

    /// <summary>Recurring price in minor units (cents).</summary>
    public int PriceInCents { get; }

    public string Currency { get; }

    /// <summary>Number of <see cref="IntervalUnit"/>s per billing cycle (e.g. 1).</summary>
    public int Interval { get; }

    /// <summary>Billing cycle unit, e.g. "month" or "day".</summary>
    public string IntervalUnit { get; }

    /// <summary>Recurring price in major units (e.g. 299.00).</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>Human-friendly price, e.g. "$299.00/month".</summary>
    public string DisplayPrice
    {
        get
        {
            var symbol = Currency == "USD" ? "$" : Currency + " ";
            var period = Interval == 1 ? IntervalUnit : $"{Interval} {IntervalUnit}s";
            return $"{symbol}{Price.ToString("0.00", CultureInfo.InvariantCulture)}/{period}";
        }
    }
}
