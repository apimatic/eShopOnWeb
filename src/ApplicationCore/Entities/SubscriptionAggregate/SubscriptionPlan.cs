using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A recurring plan a customer can subscribe to. Mirrors a provider product/price-point,
/// expressed in provider-agnostic terms. // ValueObject
/// </summary>
public class SubscriptionPlan
{
    public SubscriptionPlan(string handle, string name, decimal priceInCents, string intervalUnit, int interval)
    {
        Guard.Against.NullOrEmpty(handle, nameof(handle));
        Guard.Against.NullOrEmpty(name, nameof(name));
        Guard.Against.NullOrEmpty(intervalUnit, nameof(intervalUnit));

        Handle = handle;
        Name = name;
        PriceInCents = priceInCents;
        IntervalUnit = intervalUnit;
        Interval = interval;
    }

    public string Handle { get; private set; }
    public string Name { get; private set; }
    public decimal PriceInCents { get; private set; }
    public string IntervalUnit { get; private set; }
    public int Interval { get; private set; }
}
