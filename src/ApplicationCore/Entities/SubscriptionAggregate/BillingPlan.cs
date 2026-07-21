using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A recurring plan a customer can subscribe to, read back from the billing provider's catalog.
/// </summary>
public class BillingPlan
{
    public BillingPlan(int id, string handle, string name, long priceInCents, string intervalUnit, int interval, bool requiresPaymentMethod)
    {
        Guard.Against.NegativeOrZero(id, nameof(id));
        Guard.Against.NullOrEmpty(handle, nameof(handle));
        Guard.Against.NullOrEmpty(name, nameof(name));

        Id = id;
        Handle = handle;
        Name = name;
        PriceInCents = priceInCents;
        IntervalUnit = intervalUnit;
        Interval = interval;
        RequiresPaymentMethod = requiresPaymentMethod;
    }

    public int Id { get; }
    public string Handle { get; }
    public string Name { get; }

    /// <summary>Price in integer cents - never parse a display string for arithmetic.</summary>
    public long PriceInCents { get; }
    public string IntervalUnit { get; }
    public int Interval { get; }
    public bool RequiresPaymentMethod { get; }
}
