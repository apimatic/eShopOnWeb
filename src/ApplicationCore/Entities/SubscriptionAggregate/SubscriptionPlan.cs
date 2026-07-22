namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A provider-agnostic representation of a recurring plan a customer can subscribe to
/// (a Maxio "product"). Prices are expressed in whole currency units (dollars), never in cents —
/// the Infrastructure client is responsible for converting the provider's cents to this shape.
/// </summary>
public class SubscriptionPlan
{
    public SubscriptionPlan(int id, string handle, string name, string? description,
        decimal price, string interval, int intervalCount, bool requiresPaymentMethod)
    {
        Id = id;
        Handle = handle;
        Name = name;
        Description = description;
        Price = price;
        Interval = interval;
        IntervalCount = intervalCount;
        RequiresPaymentMethod = requiresPaymentMethod;
    }

    /// <summary>The provider-assigned numeric id (not stable across re-seeds).</summary>
    public int Id { get; }

    /// <summary>The durable API handle (e.g. <c>eshop-pro</c>).</summary>
    public string Handle { get; }

    public string Name { get; }

    public string? Description { get; }

    /// <summary>Recurring price in whole currency units (dollars), e.g. 299.00.</summary>
    public decimal Price { get; }

    /// <summary>The billing interval unit, e.g. <c>month</c>.</summary>
    public string Interval { get; }

    /// <summary>How many <see cref="Interval"/> units make up one billing period (usually 1).</summary>
    public int IntervalCount { get; }

    public bool RequiresPaymentMethod { get; }
}
