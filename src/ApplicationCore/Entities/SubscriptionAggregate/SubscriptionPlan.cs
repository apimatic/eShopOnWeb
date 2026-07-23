using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A recurring plan a customer can subscribe to, as offered by the billing provider.
/// </summary>
public class SubscriptionPlan
{
    public SubscriptionPlan(int id,
        string handle,
        string name,
        string? description,
        long priceInCents,
        int interval,
        string intervalUnit,
        bool requiresPaymentMethod,
        string? productFamilyHandle)
    {
        Guard.Against.NullOrWhiteSpace(handle, nameof(handle));
        Guard.Against.NullOrWhiteSpace(name, nameof(name));
        Guard.Against.NullOrWhiteSpace(intervalUnit, nameof(intervalUnit));
        Guard.Against.Negative(priceInCents, nameof(priceInCents));

        Id = id;
        Handle = handle;
        Name = name;
        Description = description;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
        RequiresPaymentMethod = requiresPaymentMethod;
        ProductFamilyHandle = productFamilyHandle;
    }

    /// <summary>The provider-assigned product id. Not stable across a sandbox re-seed.</summary>
    public int Id { get; }

    /// <summary>The durable identifier for this plan (e.g. <c>eshop-pro</c>).</summary>
    public string Handle { get; }

    public string Name { get; }

    public string? Description { get; }

    /// <summary>The recurring price in minor currency units (cents), as the provider stores it.</summary>
    public long PriceInCents { get; }

    /// <summary>The recurring price in major currency units (e.g. 29900 cents => 299.00).</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>How many <see cref="IntervalUnit"/>s a billing period spans.</summary>
    public int Interval { get; }

    /// <summary>The billing interval unit as the provider reports it (e.g. <c>month</c>).</summary>
    public string IntervalUnit { get; }

    public bool RequiresPaymentMethod { get; }

    public string? ProductFamilyHandle { get; }
}
