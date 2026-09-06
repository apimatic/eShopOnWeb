namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A plan a shopper can subscribe to. Mirrors a product in the billing system of record;
/// the <see cref="Handle"/> is the stable identifier, numeric ids are not.
/// </summary>
public class SubscriptionPlan
{
    public SubscriptionPlan(string handle,
        string name,
        string? description,
        int priceInCents,
        int interval,
        string intervalUnit,
        string? pricePointHandle,
        bool requiresPaymentMethod,
        string? productFamilyHandle)
    {
        Handle = handle;
        Name = name;
        Description = description;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
        PricePointHandle = pricePointHandle;
        RequiresPaymentMethod = requiresPaymentMethod;
        ProductFamilyHandle = productFamilyHandle;
    }

    /// <summary>The stable API handle of the plan, e.g. "eshop-pro". Used to subscribe.</summary>
    public string Handle { get; }
    public string Name { get; }
    public string? Description { get; }
    public int PriceInCents { get; }

    /// <summary>Billing period length, paired with <see cref="IntervalUnit"/> (e.g. 1 "month").</summary>
    public int Interval { get; }
    public string IntervalUnit { get; }

    /// <summary>Handle of the plan's default price point, when one is published.</summary>
    public string? PricePointHandle { get; }

    /// <summary>True when the billing system demands a stored card before a subscription can be created.</summary>
    public bool RequiresPaymentMethod { get; }
    public string? ProductFamilyHandle { get; }

    public decimal Price => PriceInCents / 100m;
}
