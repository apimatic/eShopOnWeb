using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingSubscription;

/// <summary>
/// A recurring subscription plan a shopper can subscribe to. In Maxio Advanced Billing terms this
/// maps to a Product within the configured Product Family. Handles are stable; numeric ids are not.
/// </summary>
public class SubscriptionPlan
{
    public SubscriptionPlan(int productId, string handle, string name, string? description,
        int priceInCents, string currencyCode, int interval, string intervalUnit)
    {
        ProductId = productId;
        Handle = handle;
        Name = name;
        Description = description;
        PriceInCents = priceInCents;
        CurrencyCode = currencyCode;
        Interval = interval;
        IntervalUnit = intervalUnit;
    }

    /// <summary>Maxio product id. Not stable across catalog re-seeds; prefer <see cref="Handle"/>.</summary>
    public int ProductId { get; }

    /// <summary>Stable API handle used to reference the plan (e.g. "eshop-pro").</summary>
    public string Handle { get; }

    public string Name { get; }

    public string? Description { get; }

    /// <summary>Recurring price in integer cents, exactly as returned by Maxio.</summary>
    public int PriceInCents { get; }

    /// <summary>ISO currency code of the price (e.g. "USD").</summary>
    public string CurrencyCode { get; }

    /// <summary>The numerical billing interval, e.g. 1.</summary>
    public int Interval { get; }

    /// <summary>The interval unit, either "month" or "day".</summary>
    public string IntervalUnit { get; }

    /// <summary>Human-friendly price such as "299.00" (major units, two decimals).</summary>
    public string FormattedPrice => (PriceInCents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
}
