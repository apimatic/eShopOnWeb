using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A recurring plan a customer can subscribe to. The handle is the durable identifier;
/// the numeric id is assigned by the billing provider and is not stable across re-seeds.
/// </summary>
public class SubscriptionPlan
{
    public SubscriptionPlan(int id, string handle, string name, decimal price, int interval,
        string intervalUnit, bool requiresPaymentMethod)
    {
        Guard.Against.NullOrEmpty(handle, nameof(handle));
        Guard.Against.Negative(price, nameof(price));

        Id = id;
        Handle = handle;
        Name = name;
        Price = price;
        Interval = interval;
        IntervalUnit = intervalUnit;
        RequiresPaymentMethod = requiresPaymentMethod;
    }

    public int Id { get; private set; }

    /// <summary>The durable API handle of the plan, e.g. <c>eshop-pro</c>.</summary>
    public string Handle { get; private set; }

    public string Name { get; private set; }

    /// <summary>The recurring price in the plan's currency (major units, e.g. 299.00 dollars).</summary>
    public decimal Price { get; private set; }

    /// <summary>The numerical billing interval, e.g. 1 when coupled with an interval unit of month.</summary>
    public int Interval { get; private set; }

    /// <summary>The billing interval unit, either <c>month</c> or <c>day</c>.</summary>
    public string IntervalUnit { get; private set; }

    /// <summary>Whether the provider demands a payment method before a subscription can be created.</summary>
    public bool RequiresPaymentMethod { get; private set; }
}
